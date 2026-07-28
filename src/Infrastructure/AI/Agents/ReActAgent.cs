using System.Text.Json;
using Anthropic.SDK;
using Anthropic.SDK.Messaging;
using Domain.AI.Agents;
using Domain.AI.Tools;
using Microsoft.Extensions.Logging;

namespace Infrastructure.AI.Agents;

/// <summary>
/// ReAct Agent — loop Reason → Act → Observe explícito e auditável.
///
/// Diferenças vs AuthorizedToolOrchestrator (3.2):
///   - AgentState é first-class citizen — não apenas List&lt;Message&gt;
///   - Cada step é observável via IAgentObserver
///   - Scratchpad estruturado: Thought / Action / Observation tipados
///   - System prompt instrui o modelo a raciocinar antes de agir
/// </summary>
public sealed class ReActAgent
{
    private readonly AnthropicClient           _client;
    private readonly IToolRegistry             _registry;
    private readonly IToolAuthorizationService _authService;
    private readonly IAgentObserver            _observer;
    private readonly ILogger<ReActAgent>       _logger;

    private const string Model         = "claude-sonnet-4-6";
    private const int    MaxIterations = 8;

    private static readonly string SystemPrompt = """
        Você é um agente especialista em operações de gateway de pagamento.

        PROTOCOLO OBRIGATÓRIO:
        1. Antes de qualquer ação, raciocine explicitamente sobre o que você sabe e o que precisa descobrir.
        2. Use tools para coletar evidências — nunca assuma dados que não foram confirmados.
        3. Após cada resultado de tool, reflita sobre o que o resultado significa para o objetivo.
        4. Quando tiver informações suficientes, entregue uma resposta final clara e estruturada.

        DOMÍNIO:
        - IDs de transação seguem o padrão TXN-XXXXXXXX
        - Estornos são irreversíveis após confirmação do PSP
        - Sempre confirme status da transação antes de recomendar ação destrutiva
        """;

    public ReActAgent(
        AnthropicClient           client,
        IToolRegistry             registry,
        IToolAuthorizationService authService,
        IAgentObserver            observer,
        ILogger<ReActAgent>       logger)
    {
        _client      = client;
        _registry    = registry;
        _authService = authService;
        _observer    = observer;
        _logger      = logger;
    }

    public async Task<AgentState> RunAsync(
        string               goal,
        ToolExecutionContext context,
        CancellationToken    ct = default)
    {
        var state = new AgentState
        {
            Goal     = goal,
            TenantId = context.TenantId,
            UserId   = context.UserId
        };

        // Camada 1: Visibilidade — LLM vê apenas tools autorizadas
        var visibleTools = _authService.FilterVisible(_registry.GetDefinitions(), context);

        var tools = visibleTools.Select(t => new Anthropic.SDK.Common.Tool(
            new Anthropic.SDK.Common.Function(t.Name, t.Description, t.InputSchema))).ToList();

        var messages = new List<Message>
        {
            new(RoleType.User, goal)
        };

        for (int iter = 0; iter < MaxIterations && !state.IsComplete; iter++)
        {
            var response = await _client.Messages.GetClaudeMessageAsync(
                new MessageParameters
                {
                    Model     = Model,
                    MaxTokens = 4096,
                    System    = [new SystemMessage(SystemPrompt)],
                    Tools     = tools.Count > 0 ? tools : null,
                    Messages  = messages
                }, ct);

            messages.Add(new Message { Role = RoleType.Assistant, Content = response.Content });

            // ── THOUGHT ──────────────────────────────────────────────────
            var thoughtText = response.Content
                .OfType<TextContent>()
                .Select(b => b.Text)
                .FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(thoughtText))
            {
                var thoughtStep = new AgentStep
                {
                    StepType = AgentStepType.Thought,
                    Content  = thoughtText
                };
                state = state.WithStep(thoughtStep);
                await _observer.OnStepAsync(state, thoughtStep, ct);
            }

            // ── FINAL ANSWER ──────────────────────────────────────────────
            if (response.StopReason == "end_turn")
            {
                state = state.WithFinalAnswer(thoughtText ?? string.Empty);
                await _observer.OnCompleteAsync(state, ct);
                return state;
            }

            if (response.StopReason != "tool_use") break;

            // ── ACTION + OBSERVATION ──────────────────────────────────────
            var toolUseBlocks = response.Content.OfType<ToolUseContent>().ToList();
            var toolResults   = new List<ToolResultContent>(toolUseBlocks.Count);

            foreach (var toolUse in toolUseBlocks)
            {
                // Action step — auditável
                var actionStep = new AgentStep
                {
                    StepType      = AgentStepType.Action,
                    Content       = $"Chamando tool '{toolUse.Name}'",
                    ToolName      = toolUse.Name,
                    ToolInputJson = toolUse.Input?.ToJsonString()
                };
                state = state.WithStep(actionStep);
                await _observer.OnStepAsync(state, actionStep, ct);

                // Camada 2: Defense-in-depth no dispatch
                var authResult = _authService.Authorize(toolUse.Name, context);

                string observationContent;

                if (!authResult.IsAuthorized)
                {
                    observationContent = JsonSerializer.Serialize(new
                    {
                        error  = "not_authorized",
                        reason = authResult.DenialReason,
                        tool   = toolUse.Name
                    });
                }
                else
                {
                    try
                    {
                        observationContent = await _registry.DispatchAsync(toolUse.Name, toolUse.Input, ct);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Tool dispatch failed: {Tool}", toolUse.Name);
                        observationContent = JsonSerializer.Serialize(new
                        {
                            error   = "execution_failed",
                            tool    = toolUse.Name,
                            message = "Erro interno ao executar a tool."
                        });
                    }
                }

                // Observation step — resultado re-injetado no contexto
                var observationStep = new AgentStep
                {
                    StepType = AgentStepType.Observation,
                    Content  = observationContent,
                    ToolName = toolUse.Name
                };
                state = state.WithStep(observationStep);
                await _observer.OnStepAsync(state, observationStep, ct);

                toolResults.Add(new ToolResultContent
                {
                    ToolUseId = toolUse.Id,
                    Content   = [new TextContent { Text = observationContent }]
                });
            }

            messages.Add(new Message
            {
                Role    = RoleType.User,
                Content = toolResults.Cast<ContentBase>().ToList()
            });
        }

        // Circuit breaker — MaxIterations atingido
        if (!state.IsComplete)
        {
            const string timeout = "Agente não convergiu dentro do limite de iterações.";
            state = state.WithFinalAnswer(timeout);
            await _observer.OnCompleteAsync(state, ct);
        }

        return state;
    }
}
