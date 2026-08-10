namespace Infrastructure.AI.Observability;

public static class GenAiConventions
{
    public const string ActivitySourceName = "PSP.AIHarness";
    public const string MeterName          = "PSP.AIHarness";

    // --- Spec OTel GenAI (EXPERIMENTAL — ponto único de atualização) ---
    public const string System           = "gen_ai.system";
    public const string OperationName    = "gen_ai.operation.name";
    public const string RequestModel     = "gen_ai.request.model";
    public const string RequestMaxTokens = "gen_ai.request.max_tokens";
    public const string ResponseModel    = "gen_ai.response.model";
    public const string FinishReasons    = "gen_ai.response.finish_reasons";
    public const string InputTokens      = "gen_ai.usage.input_tokens";
    public const string OutputTokens     = "gen_ai.usage.output_tokens";
    public const string ToolName         = "gen_ai.tool.name";

    // Conteúdo vai em EVENT, não attribute: backends indexam attributes.
    public const string EventPrompt     = "gen_ai.content.prompt";
    public const string EventCompletion = "gen_ai.content.completion";

    // --- Extensões do domínio PSP ---
    public const string TenantId       = "psp.tenant_id";
    public const string UserId         = "psp.user_id";
    public const string CorrelationId  = "psp.correlation_id";
    public const string IterationCount = "psp.ai.iteration_count";
    public const string IterationIndex = "psp.ai.iteration_index";
    public const string CostUsd        = "psp.ai.cost_usd";

    // --- Tags de DECISÃO de sampling (contrato com o Collector) ---
    // SEMPRE string. bool serializa como boolValue e não casa com
    // string_attribute no tail_sampling.
    public const string WasFallback = "psp.ai.was_fallback";
    public const string Anomalous   = "psp.ai.anomalous";
    public const string AuthzDenied = "psp.ai.authz_denied";
}
