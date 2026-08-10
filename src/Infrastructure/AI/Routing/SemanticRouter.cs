using System.Text.Json;
using Domain.AI.LLM;
using Domain.AI.Routing;
using Domain.AI.Tools;
using Infrastructure.AI.Observability;
using Infrastructure.AI.Routing.Handlers;
using Microsoft.Extensions.Logging;

namespace Infrastructure.AI.Routing;

public sealed class SemanticRouter
{
    private readonly ILLMClient                _client;
    private readonly IReadOnlyList<IAgentHandler> _handlers;
    private readonly FallbackHandler           _fallback;
    private readonly IAgentDiagnostics         _diagnostics;
    private readonly TokenUsageAccumulator     _usage;
    private readonly ILogger<SemanticRouter>   _logger;

    private const string RouterModel = "claude-haiku-4-5-20251001";

    private static readonly string RouterSystemPrompt = """
        Você é um classificador de intenção para operações de gateway de pagamento.

        INTENTS DISPONÍVEIS:
        - "investigate" → o usuário quer coletar dados e investigar uma transação/merchant
        - "analyze"     → o usuário quer análise de risco ou avaliação com dados já fornecidos
        - "summarize"   → o usuário quer um resumo ou visão geral de informações
        - "escalate"    → o usuário quer acionar revisão humana ou reportar problema crítico

        RETORNE EXCLUSIVAMENTE JSON válido — sem texto fora do JSON:
        {
          "intent": "<um dos quatro intents acima>",
          "confidence": <número entre 0.0 e 1.0>,
          "reasoning": "<explicação em uma frase>",
          "params": {
            "<chave extraída>": "<valor extraído do input>"
          }
        }

        Se não conseguir classificar com confidence >= 0.50, use o intent mais próximo
        com confidence real — nunca infle o score.
        """;

    public SemanticRouter(
        ILLMClient                 client,
        IEnumerable<IAgentHandler> handlers,
        FallbackHandler            fallback,
        IAgentDiagnostics          diagnostics,
        TokenUsageAccumulator      usage,
        ILogger<SemanticRouter>    logger)
    {
        _client      = client;
        _handlers    = handlers.ToList();
        _fallback    = fallback;
        _diagnostics = diagnostics;
        _usage       = usage;
        _logger      = logger;
    }

    public async Task<RoutedResponse> RouteAsync(
        string               input,
        ToolExecutionContext context,
        CancellationToken    ct = default)
    {
        var decision = await ClassifyAsync(input, ct);

        _logger.LogInformation(
            "[Router] intent={Intent} confidence={Score:F2} tenant={Tenant} | {Reasoning}",
            decision.Intent, decision.Confidence, context.TenantId, decision.Reasoning);

        return await RouteWithDecisionAsync(input, decision, context, ct);
    }

    /// <summary>
    /// Bypass do LLM classifier — recebe RouteDecision já construída.
    /// Usado pelo Harness no ForceIntent e em testes unitários.
    /// </summary>
    public async Task<RoutedResponse> RouteWithDecisionAsync(
        string               input,
        RouteDecision        decision,
        ToolExecutionContext context,
        CancellationToken    ct = default)
    {
        var handler = _handlers.FirstOrDefault(h => h.TargetIntent == decision.Intent);

        if (handler is null)
        {
            _logger.LogWarning("[Router] Intent desconhecido: {Intent} → fallback", decision.Intent);
            return await _fallback.HandleAsync(input, decision, context, ct);
        }

        if (decision.Confidence < handler.ConfidenceThreshold)
        {
            _logger.LogWarning(
                "[Router] Confidence {Score:F2} < threshold {Threshold:F2} intent={Intent} → fallback",
                decision.Confidence, handler.ConfidenceThreshold, decision.Intent);

            return await _fallback.HandleAsync(input, decision, context, ct);
        }

        _logger.LogInformation("[Router] Dispatching → {Handler}", handler.HandlerName);
        return await handler.HandleAsync(input, decision, context, ct);
    }

    private async Task<RouteDecision> ClassifyAsync(string input, CancellationToken ct)
    {
        using var modelSpan = _diagnostics.StartModelCall(RouterModel, 512);

        var response = await _client.CompleteAsync(new LLMRequest
        {
            Model     = RouterModel,
            MaxTokens = 512,
            System    = RouterSystemPrompt,
            Messages  = [LLMMessage.User(input)]
        }, ct);

        modelSpan?.SetTag(GenAiConventions.InputTokens,  response.InputTokens);
        modelSpan?.SetTag(GenAiConventions.OutputTokens, response.OutputTokens);
        _usage.Record(response.Model ?? RouterModel, response.InputTokens, response.OutputTokens);

        var rawJson = response.Text
            ?? throw new InvalidOperationException("Router retornou resposta vazia.");

        try
        {
            var clean = rawJson.Replace("```json", "").Replace("```", "").Trim();
            return JsonSerializer.Deserialize<RouteDecision>(clean,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new JsonException("Deserialização retornou null.");
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "[Router] Parse error — usando fallback decision");

            return new RouteDecision
            {
                Intent     = "unknown",
                Confidence = 0.0,
                Reasoning  = $"Parse error: {ex.Message}"
            };
        }
    }
}
