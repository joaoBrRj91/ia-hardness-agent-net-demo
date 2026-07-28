---
title: "MOD4 — Agentic Design Patterns — Implementação Completa"
date: 2026-07-23
tags:
  - genai
  - agentic
  - react
  - reflection
  - routing
  - facade
  - dotnet
  - clean-architecture
  - ai-harness
module: 4
---

# MOD4 — Agentic Design Patterns — Implementação Completa

## Estrutura do Projeto

```
Domain/
  AI/
    Agents/
      ReactContracts.cs          → AgentStep, AgentState, IAgentObserver
      ReflectionContracts.cs     → CriticFeedback, ReflectionState
    Routing/
      RoutingContracts.cs        → RouteDecision, RoutedResponse, IAgentHandler
    Harness/
      HarnessContracts.cs        → HarnessRequest, HarnessResponse, IAIHarness
    RAG/
      IPromptEnricher.cs         → EnrichmentResult, IPromptEnricher (Módulo 2)

Infrastructure/
  AI/
    Agents/
      LoggingAgentObserver.cs    → IAgentObserver (logging)
      ReActAgent.cs              → loop ReAct explícito (4.1)
      ReflectionAgent.cs         → Generator + Critic (4.2)
    Routing/
      Handlers/
        AllHandlers.cs           → Investigate, Analyze, Summarize, Escalate, Fallback
      SemanticRouter.cs          → classificação + dispatch (4.3)
    Harness/
      AIHarness.cs               → facade IAIHarness (4.4)
  DependencyInjection.cs         → AddAIHarness() completo
```

---

## DOMAIN/AI/AGENTS/REACTCONTRACTS.CS

```csharp
namespace Domain.AI.Agents;

public enum AgentStepType
{
    Thought,
    Action,
    Observation,
    FinalAnswer
}

public sealed record AgentStep
{
    public required AgentStepType StepType      { get; init; }
    public required string        Content       { get; init; }
    public          string?       ToolName      { get; init; }
    public          string?       ToolInputJson { get; init; }
    public          DateTime      Timestamp     { get; init; } = DateTime.UtcNow;
}

public sealed record AgentState
{
    public required string          Goal           { get; init; }
    public required string          TenantId       { get; init; }
    public required string          UserId         { get; init; }
    public          List<AgentStep> Steps          { get; init; } = [];
    public          int             IterationCount { get; init; } = 0;
    public          bool            IsComplete     { get; init; } = false;
    public          string?         FinalAnswer    { get; init; }

    /// <summary>
    /// Imutável: retorna novo estado com step appended.
    /// Pattern: Event Sourcing light — cada mutação produz novo estado.
    /// </summary>
    public AgentState WithStep(AgentStep step) => this with
    {
        Steps          = [..Steps, step],
        IterationCount = step.StepType == AgentStepType.Action
            ? IterationCount + 1
            : IterationCount
    };

    public AgentState WithFinalAnswer(string answer) => this with
    {
        IsComplete  = true,
        FinalAnswer = answer,
        Steps       = [..Steps, new AgentStep
        {
            StepType = AgentStepType.FinalAnswer,
            Content  = answer
        }]
    };
}

/// <summary>
/// Hook de observabilidade — implementações:
///   LoggingAgentObserver      (Módulo 4)
///   OpenTelemetryObserver     (Módulo 6)
///   TestObserver              (testes unitários)
/// </summary>
public interface IAgentObserver
{
    Task OnStepAsync(AgentState state, AgentStep step, CancellationToken ct = default);
    Task OnCompleteAsync(AgentState finalState, CancellationToken ct = default);
}
```

---

## DOMAIN/AI/AGENTS/REFLECTIONCONTRACTS.CS

```csharp
namespace Domain.AI.Agents;

/// <summary>
/// Contrato tipado entre Generator e Critic.
/// Serializado como Structured Output — o Critic DEVE retornar este schema.
/// </summary>
public sealed record CriticFeedback
{
    /// <summary>0.0 (inaceitável) → 1.0 (perfeito).</summary>
    [JsonPropertyName("score")]
    public required double Score { get; init; }

    /// <summary>Score >= threshold do domínio — pré-computado pelo Critic.</summary>
    [JsonPropertyName("is_acceptable")]
    public required bool IsAcceptable { get; init; }

    /// <summary>Problemas concretos identificados no Draft.</summary>
    [JsonPropertyName("issues")]
    public required List<string> Issues { get; init; }

    /// <summary>Sugestões específicas de melhoria para o Generator.</summary>
    [JsonPropertyName("suggestions")]
    public required List<string> Suggestions { get; init; }

    /// <summary>Raciocínio do Critic — auditável.</summary>
    [JsonPropertyName("reasoning")]
    public required string Reasoning { get; init; }
}

public sealed record ReflectionState
{
    public required string               Goal            { get; init; }
    public required string               TenantId        { get; init; }
    public          string?              CurrentDraft    { get; init; }
    public          List<CriticFeedback> FeedbackHistory { get; init; } = [];
    public          int                  RefinementCount { get; init; } = 0;
    public          bool                 IsAccepted      { get; init; } = false;
    public          string?              FinalOutput     { get; init; }

    public ReflectionState WithDraft(string draft)
        => this with { CurrentDraft = draft };

    public ReflectionState WithFeedback(CriticFeedback feedback) => this with
    {
        FeedbackHistory = [..FeedbackHistory, feedback],
        RefinementCount = RefinementCount + 1,
        IsAccepted      = feedback.IsAcceptable,
        FinalOutput     = feedback.IsAcceptable ? CurrentDraft : null
    };
}
```

---

## DOMAIN/AI/ROUTING/ROUTINGCONTRACTS.CS

```csharp
namespace Domain.AI.Routing;

public sealed record RouteDecision
{
    [JsonPropertyName("intent")]
    public required string Intent { get; init; }

    [JsonPropertyName("confidence")]
    public required double Confidence { get; init; }

    [JsonPropertyName("reasoning")]
    public required string Reasoning { get; init; }

    /// <summary>
    /// Parâmetros extraídos do input pelo Router.
    /// Ex: { "transaction_id": "TXN-ABC123" }
    /// Evita re-parsear o input em cada agente downstream.
    /// </summary>
    [JsonPropertyName("params")]
    public Dictionary<string, string> Params { get; init; } = [];
}

public sealed record RoutedResponse
{
    public required string      Intent      { get; init; }
    public required double      Confidence  { get; init; }
    public required string      Output      { get; init; }
    public required string      HandlerName { get; init; }
    public required bool        WasFallback { get; init; }
    public          AgentState? AgentTrace  { get; init; }
}

/// <summary>
/// Contrato uniforme para todos os handlers do registry.
/// SemanticRouter não conhece os tipos concretos — apenas este contrato.
/// </summary>
public interface IAgentHandler
{
    string HandlerName         { get; }
    string TargetIntent        { get; }
    double ConfidenceThreshold { get; }

    Task<RoutedResponse> HandleAsync(
        string               input,
        RouteDecision        decision,
        ToolExecutionContext  context,
        CancellationToken    ct = default);
}
```

---

## DOMAIN/AI/HARNESS/HARNESSCONTRACTS.CS

```csharp
namespace Domain.AI.Harness;

public sealed record HarnessRequest
{
    public required string              Input          { get; init; }
    public required ToolExecutionContext Context        { get; init; }

    /// <summary>
    /// Se fornecido, bypassa o SemanticRouter e força o intent.
    /// Útil para chamadas programáticas onde o intent é conhecido.
    /// </summary>
    public          string?             ForceIntent    { get; init; }

    /// <summary>
    /// Propagado de sistemas upstream para distributed tracing.
    /// Gerado internamente se ausente.
    /// </summary>
    public          string?             CorrelationId  { get; init; }

    /// <summary>
    /// Se true, desativa RAG enrichment.
    /// Usar quando o input já contém contexto suficiente.
    /// </summary>
    public          bool                SkipEnrichment { get; init; } = false;
}

public sealed record HarnessResponse
{
    public required string       Output          { get; init; }
    public required string       Intent          { get; init; }
    public required string       HandlerName     { get; init; }
    public required double       ConfidenceScore { get; init; }
    public required bool         WasFallback     { get; init; }
    public required string       TenantId        { get; init; }
    public required string       CorrelationId   { get; init; }
    public required long         DurationMs      { get; init; }

    // Hooks para Módulo 6 — OTel spans e métricas
    public          int          IterationCount  { get; init; }
    public          int          StepCount       { get; init; }
    public          AgentStep[]  AgentTrace      { get; init; } = [];
    public          bool         WasEnriched     { get; init; }
    public          string?      EnrichmentQuery { get; init; }
}

public interface IAIHarness
{
    Task<HarnessResponse> ProcessAsync(
        HarnessRequest    request,
        CancellationToken ct = default);
}
```

---

## DOMAIN/AI/RAG/IPROMPTЕНRICHER.CS

```csharp
namespace Domain.AI.RAG;

// Contrato definido no Módulo 2 — reproduzido para referência de composição.

public sealed record EnrichmentResult
{
    public required string  EnrichedInput  { get; init; }
    public required bool    WasEnriched    { get; init; }
    public          string? QueryUsed      { get; init; }
    public          int     ChunksInjected { get; init; }
}

public interface IPromptEnricher
{
    Task<EnrichmentResult> EnrichAsync(
        string            input,
        string            tenantId,
        CancellationToken ct = default);
}
```

---

## INFRASTRUCTURE/AI/AGENTS/LOGGINGAGENTOBSERVER.CS

```csharp
namespace Infrastructure.AI.Agents;

public sealed class LoggingAgentObserver : IAgentObserver
{
    private readonly ILogger<LoggingAgentObserver> _logger;

    public LoggingAgentObserver(ILogger<LoggingAgentObserver> logger)
        => _logger = logger;

    public Task OnStepAsync(AgentState state, AgentStep step, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "[ReAct][{StepType}] iter={Iter} tenant={Tenant} | {Content}",
            step.StepType,
            state.IterationCount,
            state.TenantId,
            step.Content.Length > 200 ? step.Content[..200] + "…" : step.Content);

        return Task.CompletedTask;
    }

    public Task OnCompleteAsync(AgentState finalState, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "[ReAct][Complete] iter={Iter} tenant={Tenant} steps={Steps} | answer_len={Len}",
            finalState.IterationCount,
            finalState.TenantId,
            finalState.Steps.Count,
            finalState.FinalAnswer?.Length ?? 0);

        return Task.CompletedTask;
    }
}
```

---

## INFRASTRUCTURE/AI/AGENTS/REACTAGENT.CS

```csharp
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
        ToolExecutionContext  context,
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

        var tools = visibleTools.Select(t => new Tool
        {
            Name        = t.Name,
            Description = t.Description,
            InputSchema = t.InputSchema
        }).ToList();

        var messages = new List<Message>
        {
            new() { Role = RoleType.User, Content = goal }
        };

        for (int iter = 0; iter < MaxIterations && !state.IsComplete; iter++)
        {
            var response = await _client.Messages.GetClaudeMessageAsync(
                new MessageParameters
                {
                    Model     = Model,
                    MaxTokens = 4096,
                    System    = SystemPrompt,
                    Tools     = tools.Count > 0 ? tools : null,
                    Messages  = messages
                }, ct);

            messages.Add(new Message { Role = RoleType.Assistant, Content = response.Content });

            // ── THOUGHT ──────────────────────────────────────────────────
            var thoughtText = response.Content
                .OfType<TextBlock>()
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
            var toolUseBlocks = response.Content.OfType<ToolUseBlock>().ToList();
            var toolResults   = new List<ToolResultBlock>(toolUseBlocks.Count);

            foreach (var toolUse in toolUseBlocks)
            {
                // Action step — auditável
                var actionStep = new AgentStep
                {
                    StepType      = AgentStepType.Action,
                    Content       = $"Chamando tool '{toolUse.Name}'",
                    ToolName      = toolUse.Name,
                    ToolInputJson = toolUse.Input.ToJsonString()
                };
                state = state.WithStep(actionStep);
                await _observer.OnStepAsync(state, actionStep, ct);

                // Camada 2: Defense-in-depth no dispatch
                var authResult = _authService.Authorize(toolUse.Name, context);

                string observationContent;
                bool   isError = false;

                if (!authResult.IsAuthorized)
                {
                    observationContent = JsonSerializer.Serialize(new
                    {
                        error  = "not_authorized",
                        reason = authResult.DenialReason,
                        tool   = toolUse.Name
                    });
                    isError = true;
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
                        isError = true;
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

                toolResults.Add(new ToolResultBlock
                {
                    ToolUseId = toolUse.Id,
                    Content   = observationContent,
                    IsError   = isError
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
```

---

## INFRASTRUCTURE/AI/AGENTS/REFLECTIONAGENT.CS

```csharp
namespace Infrastructure.AI.Agents;

public sealed class ReflectionAgent
{
    private readonly AnthropicClient          _client;
    private readonly IAgentObserver           _observer;
    private readonly ILogger<ReflectionAgent> _logger;

    private const string GeneratorModel      = "claude-sonnet-4-6";
    private const string CriticModel         = "claude-haiku-4-5-20251001";
    private const int    MaxRefinements      = 3;
    private const double AcceptanceThreshold = 0.80;

    private static readonly string GeneratorSystemPrompt = """
        Você é um analista sênior de operações de gateway de pagamento.

        Sua função é produzir análises precisas, baseadas em evidências e acionáveis.

        ESTRUTURA OBRIGATÓRIA DA ANÁLISE:
        1. **Situação**: o que os dados mostram objetivamente
        2. **Avaliação**: interpretação dos fatos (distinga claramente de inferência)
        3. **Recomendação**: ação específica com justificativa
        4. **Riscos**: o que pode dar errado com a recomendação

        Quando receber feedback de revisão, incorpore TODAS as sugestões explicitamente.
        """;

    private static readonly string CriticSystemPrompt = """
        Você é um revisor especializado em análises de risco de pagamento.

        Avalie o DRAFT contra estes critérios do domínio PSP:
        1. Cita evidências concretas (IDs, valores, timestamps, status)?
        2. Distingue claramente fato de inferência?
        3. A recomendação é específica e acionável (não vaga)?
        4. Está dentro do escopo operacional de um analista de pagamentos?
        5. Identifica riscos da recomendação?

        RETORNE EXCLUSIVAMENTE JSON válido neste schema — sem texto fora do JSON:
        {
          "score": <número entre 0.0 e 1.0>,
          "is_acceptable": <true se score >= 0.80, false caso contrário>,
          "issues": ["<problema concreto 1>", "<problema concreto 2>"],
          "suggestions": ["<sugestão específica 1>", "<sugestão específica 2>"],
          "reasoning": "<explicação objetiva da pontuação>"
        }
        """;

    public ReflectionAgent(
        AnthropicClient          client,
        IAgentObserver           observer,
        ILogger<ReflectionAgent> logger)
    {
        _client   = client;
        _observer = observer;
        _logger   = logger;
    }

    public async Task<ReflectionState> RunAsync(
        string            goal,
        string            tenantId,
        CancellationToken ct = default)
    {
        var state = new ReflectionState { Goal = goal, TenantId = tenantId };

        for (int i = 0; i <= MaxRefinements; i++)
        {
            // ── GENERATION ────────────────────────────────────────────────
            var draft = await GenerateAsync(state, ct);
            state = state.WithDraft(draft);

            var draftStep = new AgentStep
            {
                StepType = AgentStepType.Thought,
                Content  = $"[Generator] Draft #{i + 1} produzido ({draft.Length} chars)"
            };
            await _observer.OnStepAsync(ToAgentState(state), draftStep, ct);

            // ── CRITIQUE ──────────────────────────────────────────────────
            var feedback = await CritiqueAsync(goal, draft, ct);
            state = state.WithFeedback(feedback);

            var critiqueStep = new AgentStep
            {
                StepType = AgentStepType.Observation,
                Content  = $"[Critic] score={feedback.Score:F2} acceptable={feedback.IsAcceptable} | {feedback.Reasoning}"
            };
            await _observer.OnStepAsync(ToAgentState(state), critiqueStep, ct);

            _logger.LogInformation(
                "[Reflection] iter={Iter} score={Score} acceptable={Ok} tenant={Tenant}",
                i + 1, feedback.Score, feedback.IsAcceptable, tenantId);

            if (feedback.IsAcceptable)
            {
                await _observer.OnCompleteAsync(ToAgentState(state), ct);
                return state;
            }
        }

        // MaxRefinements atingido — circuit breaker — aceita melhor draft
        _logger.LogWarning(
            "[Reflection] MaxRefinements atingido | tenant={Tenant} bestScore={Score}",
            tenantId, state.FeedbackHistory.Max(f => f.Score));

        state = state with { IsAccepted = true, FinalOutput = state.CurrentDraft };
        await _observer.OnCompleteAsync(ToAgentState(state), ct);
        return state;
    }

    // ── Generator ─────────────────────────────────────────────────────────

    private async Task<string> GenerateAsync(ReflectionState state, CancellationToken ct)
    {
        var response = await _client.Messages.GetClaudeMessageAsync(
            new MessageParameters
            {
                Model     = GeneratorModel,
                MaxTokens = 2048,
                System    = GeneratorSystemPrompt,
                Messages  =
                [
                    new() { Role = RoleType.User, Content = BuildGeneratorPrompt(state) }
                ]
            }, ct);

        return response.Content.OfType<TextBlock>().FirstOrDefault()?.Text
            ?? throw new InvalidOperationException("Generator retornou resposta vazia.");
    }

    private static string BuildGeneratorPrompt(ReflectionState state)
    {
        if (state.FeedbackHistory.Count == 0)
            return state.Goal;

        var lastFeedback = state.FeedbackHistory[^1];
        var issues       = string.Join("\n", lastFeedback.Issues.Select(i => $"  - {i}"));
        var suggestions  = string.Join("\n", lastFeedback.Suggestions.Select(s => $"  - {s}"));

        return $"""
            OBJETIVO ORIGINAL:
            {state.Goal}

            SEU DRAFT ANTERIOR:
            {state.CurrentDraft}

            FEEDBACK DO REVISOR (score: {lastFeedback.Score:F2}):
            Problemas identificados:
            {issues}

            Sugestões de melhoria:
            {suggestions}

            Revise o draft incorporando TODO o feedback acima.
            """;
    }

    // ── Critic ────────────────────────────────────────────────────────────

    private async Task<CriticFeedback> CritiqueAsync(
        string            goal,
        string            draft,
        CancellationToken ct)
    {
        var response = await _client.Messages.GetClaudeMessageAsync(
            new MessageParameters
            {
                Model     = CriticModel,
                MaxTokens = 1024,
                System    = CriticSystemPrompt,
                Messages  =
                [
                    new()
                    {
                        Role    = RoleType.User,
                        Content = $"""
                            OBJETIVO QUE O ANALISTA DEVERIA ATINGIR:
                            {goal}

                            DRAFT DO ANALISTA PARA AVALIAÇÃO:
                            {draft}
                            """
                    }
                ]
            }, ct);

        var rawJson = response.Content.OfType<TextBlock>().FirstOrDefault()?.Text
            ?? throw new InvalidOperationException("Critic retornou resposta vazia.");

        return ParseCriticFeedback(rawJson);
    }

    private CriticFeedback ParseCriticFeedback(string rawJson)
    {
        try
        {
            var clean = rawJson
                .Replace("```json", "")
                .Replace("```", "")
                .Trim();

            return JsonSerializer.Deserialize<CriticFeedback>(clean,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new JsonException("Deserialização retornou null.");
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "[Reflection] Critic retornou JSON inválido — usando fallback.");

            return new CriticFeedback
            {
                Score        = 0.0,
                IsAcceptable = false,
                Issues       = ["Critic retornou formato inválido — não foi possível avaliar."],
                Suggestions  = ["Revisar o draft com foco em clareza e estrutura."],
                Reasoning    = $"Parse error: {ex.Message}"
            };
        }
    }

    private static AgentState ToAgentState(ReflectionState s) => new()
    {
        Goal           = s.Goal,
        TenantId       = s.TenantId,
        UserId         = "reflection-agent",
        IterationCount = s.RefinementCount,
        IsComplete     = s.IsAccepted,
        FinalAnswer    = s.FinalOutput,
        Steps          = []
    };
}
```

---

## INFRASTRUCTURE/AI/ROUTING/HANDLERS/ALLHANDLERS.CS

```csharp
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
        ToolExecutionContext  context,
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
        ToolExecutionContext  context,
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
    private readonly AnthropicClient          _client;
    private readonly ILogger<SummarizeHandler> _logger;

    public SummarizeHandler(AnthropicClient client, ILogger<SummarizeHandler> logger)
    {
        _client = client;
        _logger = logger;
    }

    public string HandlerName         => "SummarizeHandler";
    public string TargetIntent        => "summarize";
    public double ConfidenceThreshold => 0.60;

    public async Task<RoutedResponse> HandleAsync(
        string               input,
        RouteDecision        decision,
        ToolExecutionContext  context,
        CancellationToken    ct = default)
    {
        var response = await _client.Messages.GetClaudeMessageAsync(
            new MessageParameters
            {
                Model     = "claude-haiku-4-5-20251001",
                MaxTokens = 1024,
                System    = "Você é um assistente de resumo de operações PSP. Seja conciso e objetivo.",
                Messages  = [new() { Role = RoleType.User, Content = input }]
            }, ct);

        var output = response.Content.OfType<TextBlock>().FirstOrDefault()?.Text
            ?? "Resumo indisponível.";

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
        ToolExecutionContext  context,
        CancellationToken    ct = default)
    {
        _logger.LogWarning(
            "[Escalate] Caso encaminhado | tenant={Tenant} userId={User} reason={Reason}",
            context.TenantId, context.UserId, decision.Reasoning);

        var ticketId = $"ESC-{Guid.NewGuid():N}"[..12].ToUpper();

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
        ToolExecutionContext  context,
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
```

---

## INFRASTRUCTURE/AI/ROUTING/SEMANTICROUTER.CS

```csharp
namespace Infrastructure.AI.Routing;

public sealed class SemanticRouter
{
    private readonly AnthropicClient             _client;
    private readonly IReadOnlyList<IAgentHandler> _handlers;
    private readonly FallbackHandler             _fallback;
    private readonly ILogger<SemanticRouter>     _logger;

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
        AnthropicClient            client,
        IEnumerable<IAgentHandler> handlers,
        FallbackHandler            fallback,
        ILogger<SemanticRouter>    logger)
    {
        _client   = client;
        _handlers = handlers.ToList();
        _fallback = fallback;
        _logger   = logger;
    }

    public async Task<RoutedResponse> RouteAsync(
        string               input,
        ToolExecutionContext  context,
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
        ToolExecutionContext  context,
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
        var response = await _client.Messages.GetClaudeMessageAsync(
            new MessageParameters
            {
                Model     = RouterModel,
                MaxTokens = 512,
                System    = RouterSystemPrompt,
                Messages  = [new() { Role = RoleType.User, Content = input }]
            }, ct);

        var rawJson = response.Content.OfType<TextBlock>().FirstOrDefault()?.Text
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
```

---

## INFRASTRUCTURE/AI/HARNESS/AIHARNESS.CS

```csharp
namespace Infrastructure.AI.Harness;

/// <summary>
/// Facade que unifica Módulos 2, 3, 4.1, 4.2, 4.3 em um único entry point.
///
/// Pipeline por request:
///   1. CorrelationId → gera se ausente (distributed tracing)
///   2. Enrich        → RAG injeta contexto relevante no input
///   3. Route         → SemanticRouter ou ForceIntent bypass
///   4. Observe       → HarnessResponse coleta métricas (hook Módulo 6)
/// </summary>
public sealed class AIHarness : IAIHarness
{
    private readonly SemanticRouter     _router;
    private readonly IPromptEnricher    _enricher;
    private readonly ILogger<AIHarness> _logger;

    public AIHarness(
        SemanticRouter     router,
        IPromptEnricher    enricher,
        ILogger<AIHarness> logger)
    {
        _router   = router;
        _enricher = enricher;
        _logger   = logger;
    }

    public async Task<HarnessResponse> ProcessAsync(
        HarnessRequest    request,
        CancellationToken ct = default)
    {
        var correlationId = request.CorrelationId ?? Guid.NewGuid().ToString("N")[..16];
        var sw            = Stopwatch.StartNew();

        _logger.LogInformation(
            "[Harness] START | correlationId={Id} tenant={Tenant} userId={User} forceIntent={Force}",
            correlationId, request.Context.TenantId, request.Context.UserId,
            request.ForceIntent ?? "none");

        try
        {
            // ── 1. RAG Enrichment ─────────────────────────────────────────
            EnrichmentResult enrichment;

            if (request.SkipEnrichment)
            {
                enrichment = new EnrichmentResult
                {
                    EnrichedInput  = request.Input,
                    WasEnriched    = false,
                    ChunksInjected = 0
                };
            }
            else
            {
                enrichment = await _enricher.EnrichAsync(
                    request.Input, request.Context.TenantId, ct);

                _logger.LogDebug(
                    "[Harness] Enrichment | wasEnriched={Was} chunks={Chunks} correlationId={Id}",
                    enrichment.WasEnriched, enrichment.ChunksInjected, correlationId);
            }

            // ── 2. ForceIntent bypass ou Semantic Routing ─────────────────
            RoutedResponse routedResponse;

            if (request.ForceIntent is not null)
            {
                var forcedDecision = new RouteDecision
                {
                    Intent     = request.ForceIntent,
                    Confidence = 1.0,
                    Reasoning  = "ForceIntent — bypass do SemanticRouter"
                };

                _logger.LogInformation(
                    "[Harness] ForceIntent={Intent} | correlationId={Id}",
                    request.ForceIntent, correlationId);

                routedResponse = await _router.RouteWithDecisionAsync(
                    enrichment.EnrichedInput, forcedDecision, request.Context, ct);
            }
            else
            {
                routedResponse = await _router.RouteAsync(
                    enrichment.EnrichedInput, request.Context, ct);
            }

            sw.Stop();

            // ── 3. HarnessResponse — coleta observabilidade ───────────────
            var response = new HarnessResponse
            {
                Output          = routedResponse.Output,
                Intent          = routedResponse.Intent,
                HandlerName     = routedResponse.HandlerName,
                ConfidenceScore = routedResponse.Confidence,
                WasFallback     = routedResponse.WasFallback,
                TenantId        = request.Context.TenantId,
                CorrelationId   = correlationId,
                DurationMs      = sw.ElapsedMilliseconds,
                WasEnriched     = enrichment.WasEnriched,
                EnrichmentQuery = enrichment.QueryUsed,
                IterationCount  = routedResponse.AgentTrace?.IterationCount ?? 0,
                StepCount       = routedResponse.AgentTrace?.Steps.Count    ?? 0,
                AgentTrace      = routedResponse.AgentTrace?.Steps.ToArray() ?? []
            };

            _logger.LogInformation(
                "[Harness] END | correlationId={Id} intent={Intent} handler={Handler} " +
                "confidence={Score:F2} fallback={Fallback} duration={Duration}ms steps={Steps}",
                correlationId, response.Intent, response.HandlerName, response.ConfidenceScore,
                response.WasFallback, response.DurationMs, response.StepCount);

            return response;
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex,
                "[Harness] ERROR | correlationId={Id} tenant={Tenant} duration={Duration}ms",
                correlationId, request.Context.TenantId, sw.ElapsedMilliseconds);
            throw;
        }
    }
}
```

---

## INFRASTRUCTURE/DEPENDENCYINJECTION.CS — AddAIHarness() Completo

```csharp
namespace Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAIHarness(
        this IServiceCollection services,
        IConfiguration          config)
    {
        // ── LLM Client ────────────────────────────────────────────────────
        services.AddSingleton(_ => new AnthropicClient(
            config["Anthropic:ApiKey"]
            ?? throw new InvalidOperationException("Anthropic:ApiKey não configurada")));

        // ── Módulo 3.2 — Authorization ────────────────────────────────────
        services.AddSingleton<IToolPolicy>(_ =>
        {
            var tenants = config.GetSection("AllowedTenants").Get<HashSet<string>>()
                ?? throw new InvalidOperationException("AllowedTenants não configurado");
            return new TenantIsolationPolicy((IReadOnlySet<string>)tenants);
        });
        services.AddSingleton<IToolPolicy, ReadOnlyPolicy>();
        services.AddSingleton<IToolPolicy>(_ =>
            new RateLimitPolicy(config.GetValue<int>("RateLimit:ToolCallsPerMinute", 30)));
        services.AddSingleton<IToolAuthorizationService, ToolAuthorizationService>();

        // ── Módulo 3.1 — Tools ────────────────────────────────────────────
        services.AddSingleton<IToolDefinition, CheckTransactionStatusDefinition>();
        services.AddSingleton<IToolDefinition, IssueRefundDefinition>();
        services.AddScoped<IToolHandler, CheckTransactionStatusHandler>();
        services.AddScoped<IToolHandler, IssueRefundHandler>();
        services.AddSingleton<IToolRegistry>(sp =>
        {
            var definitions = sp.GetServices<IToolDefinition>().ToList();
            var logger      = sp.GetRequiredService<ILogger<ToolRegistry>>();
            return new ToolRegistryWithScopedHandlers(definitions, sp, logger);
        });

        // ── Módulo 2 — RAG Enricher ───────────────────────────────────────
        services.AddScoped<IPromptEnricher, RagPromptEnricher>();

        // ── Módulo 4.1 — Observabilidade + ReAct ─────────────────────────
        services.AddSingleton<IAgentObserver, LoggingAgentObserver>();
        services.AddScoped<ReActAgent>();

        // ── Módulo 4.2 — Reflection ───────────────────────────────────────
        services.AddScoped<ReflectionAgent>();

        // ── Módulo 4.3 — Routing Handlers ─────────────────────────────────
        services.AddScoped<IAgentHandler, InvestigateHandler>();
        services.AddScoped<IAgentHandler, AnalyzeHandler>();
        services.AddScoped<IAgentHandler, SummarizeHandler>();
        services.AddScoped<IAgentHandler, EscalateHandler>();
        services.AddScoped<FallbackHandler>();
        services.AddScoped<SemanticRouter>();

        // ── Módulo 4.4 — Harness Facade ───────────────────────────────────
        services.AddScoped<IAIHarness, AIHarness>();

        // ── Módulo 3.3 — MCP Client ───────────────────────────────────────
        services.AddScoped<McpHostService>();

        return services;
    }
}
```

---

## Relação com outros módulos

- Consolida: [[MOD4-1-ReAct-Pattern]], [[MOD4-2-Reflection-Pattern]], [[MOD4-3-Semantic-Routing]], [[MOD4-4-Harness-Composition]]
- Depende de: [[MOD3-2-Policy-Authorization]], [[MOD3-1-Tool-Use]], [[MOD3-3-MCP]], [[MOD2-RAG-Pipeline]]
- `IAIHarness` é o entry point para: [[MOD5-LangGraph]]
- `HarnessResponse` pré-wired para: [[MOD6-LLMOps]]

---

➡️ Próximo: [[MOD5-LangGraph]]