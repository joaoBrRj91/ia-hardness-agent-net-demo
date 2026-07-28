using System.Text.Json;
using Anthropic.SDK;
using Anthropic.SDK.Messaging;
using Domain.AI.Agents;
using Microsoft.Extensions.Logging;

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
            tenantId, state.FeedbackHistory.Count > 0 ? state.FeedbackHistory.Max(f => f.Score) : 0);

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
                System    = [new SystemMessage(GeneratorSystemPrompt)],
                Messages  =
                [
                    new(RoleType.User, BuildGeneratorPrompt(state))
                ]
            }, ct);

        return response.Content.OfType<TextContent>().FirstOrDefault()?.Text
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
                System    = [new SystemMessage(CriticSystemPrompt)],
                Messages  =
                [
                    new(RoleType.User, $"""
                        OBJETIVO QUE O ANALISTA DEVERIA ATINGIR:
                        {goal}

                        DRAFT DO ANALISTA PARA AVALIAÇÃO:
                        {draft}
                        """)
                ]
            }, ct);

        var rawJson = response.Content.OfType<TextContent>().FirstOrDefault()?.Text
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
