using Domain.AI.LLM;
using Domain.AI.Routing;
using Domain.AI.Tools;
using Infrastructure.AI.Agents;
using Infrastructure.AI.Observability;
using Microsoft.Extensions.Logging;

namespace Infrastructure.AI.Routing.Handlers;

/// <summary>
/// "investigate" → ReActAgent — coleta dados via tools.
/// Threshold mais alto: custo computacional elevado se errar o intent.
/// </summary>
public sealed class InvestigateHandler : IAgentHandler
{
    private readonly ReActAgent _reactAgent;

    public InvestigateHandler(ReActAgent reactAgent) => _reactAgent = reactAgent;

    public string HandlerName         => "InvestigateHandler";
    public string TargetIntent        => "investigate";
    public double ConfidenceThreshold => 0.75;

    public async Task<RoutedResponse> HandleAsync(
        string               input,
        RouteDecision        decision,
        ToolExecutionContext context,
        CancellationToken    ct = default)
    {
        var agentState = await _reactAgent.RunAsync(input, context, ct);

        return new RoutedResponse
        {
            Intent      = decision.Intent,
            Confidence  = decision.Confidence,
            Output      = agentState.FinalAnswer ?? "Investigação inconclusiva.",
            HandlerName = HandlerName,
            WasFallback = false,
            AgentTrace  = agentState
        };
    }
}

/// <summary>
/// "analyze" → ReflectionAgent — refina qualidade da análise.
/// Útil quando o input já contém dados suficientes.
/// </summary>
public sealed class AnalyzeHandler : IAgentHandler
{
    private readonly ReflectionAgent _reflectionAgent;

    public AnalyzeHandler(ReflectionAgent reflectionAgent)
        => _reflectionAgent = reflectionAgent;

    public string HandlerName         => "AnalyzeHandler";
    public string TargetIntent        => "analyze";
    public double ConfidenceThreshold => 0.70;

    public async Task<RoutedResponse> HandleAsync(
        string               input,
        RouteDecision        decision,
        ToolExecutionContext context,
        CancellationToken    ct = default)
    {
        var state = await _reflectionAgent.RunAsync(input, context.TenantId, ct);

        return new RoutedResponse
        {
            Intent      = decision.Intent,
            Confidence  = decision.Confidence,
            Output      = state.FinalOutput ?? "Análise não concluída.",
            HandlerName = HandlerName,
            WasFallback = false
        };
    }
}

/// <summary>
/// "summarize" → single LLM call (Haiku).
/// Intent mais simples — threshold mais baixo — custo mínimo.
/// </summary>
public sealed class SummarizeHandler : IAgentHandler
{
    private const string Model = "claude-haiku-4-5-20251001";

    private readonly ILLMClient                _client;
    private readonly IAgentDiagnostics         _diagnostics;
    private readonly TokenUsageAccumulator     _usage;
    private readonly ILogger<SummarizeHandler> _logger;

    public SummarizeHandler(
        ILLMClient                client,
        IAgentDiagnostics         diagnostics,
        TokenUsageAccumulator     usage,
        ILogger<SummarizeHandler> logger)
    {
        _client      = client;
        _diagnostics = diagnostics;
        _usage       = usage;
        _logger      = logger;
    }

    public string HandlerName         => "SummarizeHandler";
    public string TargetIntent        => "summarize";
    public double ConfidenceThreshold => 0.60;

    public async Task<RoutedResponse> HandleAsync(
        string               input,
        RouteDecision        decision,
        ToolExecutionContext context,
        CancellationToken    ct = default)
    {
        string output;
        using (var modelSpan = _diagnostics.StartModelCall(Model, 1024))
        {
            var response = await _client.CompleteAsync(new LLMRequest
            {
                Model     = Model,
                MaxTokens = 1024,
                System    = "Você é um assistente de resumo de operações PSP. Seja conciso e objetivo.",
                Messages  = [LLMMessage.User(input)]
            }, ct);

            modelSpan?.SetTag(GenAiConventions.InputTokens,  response.InputTokens);
            modelSpan?.SetTag(GenAiConventions.OutputTokens, response.OutputTokens);
            _usage.Record(response.Model ?? Model, response.InputTokens, response.OutputTokens);

            output = response.Text ?? "Resumo indisponível.";
        }

        return new RoutedResponse
        {
            Intent      = decision.Intent,
            Confidence  = decision.Confidence,
            Output      = output,
            HandlerName = HandlerName,
            WasFallback = false
        };
    }
}

/// <summary>
/// "escalate" → handoff para fila humana — sem LLM call.
/// Em produção: publica em SQS/SNS com payload estruturado.
/// </summary>
public sealed class EscalateHandler : IAgentHandler
{
    private readonly ILogger<EscalateHandler> _logger;

    public EscalateHandler(ILogger<EscalateHandler> logger) => _logger = logger;

    public string HandlerName         => "EscalateHandler";
    public string TargetIntent        => "escalate";
    public double ConfidenceThreshold => 0.85;

    public Task<RoutedResponse> HandleAsync(
        string               input,
        RouteDecision        decision,
        ToolExecutionContext context,
        CancellationToken    ct = default)
    {
        _logger.LogWarning(
            "[Escalate] Caso encaminhado | tenant={Tenant} userId={User} reason={Reason}",
            context.TenantId, context.UserId, decision.Reasoning);

        var ticketId = $"ESC-{Guid.NewGuid():N}"[..12].ToUpperInvariant();

        return Task.FromResult(new RoutedResponse
        {
            Intent      = decision.Intent,
            Confidence  = decision.Confidence,
            Output      = $"Caso escalado para análise humana. Ticket: {ticketId}",
            HandlerName = HandlerName,
            WasFallback = false
        });
    }
}

/// <summary>
/// Fallback — confidence abaixo do threshold.
/// Resposta conservadora: nunca chama tools, nunca toma ação destrutiva.
/// </summary>
public sealed class FallbackHandler : IAgentHandler
{
    public string HandlerName         => "FallbackHandler";
    public string TargetIntent        => "__fallback__";
    public double ConfidenceThreshold => 0.0;

    public Task<RoutedResponse> HandleAsync(
        string               input,
        RouteDecision        decision,
        ToolExecutionContext context,
        CancellationToken    ct = default)
    {
        var output = $"Não foi possível determinar a intenção com confiança suficiente " +
                     $"(score: {decision.Confidence:F2}). Por favor, reformule a solicitação " +
                     $"ou especifique a operação desejada: investigar, analisar, resumir ou escalar.";

        return Task.FromResult(new RoutedResponse
        {
            Intent      = decision.Intent,
            Confidence  = decision.Confidence,
            Output      = output,
            HandlerName = HandlerName,
            WasFallback = true
        });
    }
}
