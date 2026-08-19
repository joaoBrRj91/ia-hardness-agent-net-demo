namespace Infrastructure.AI.Observability;

public static class GenAiConventions
{
    public const string ActivitySourceName = "App.AIHarness";
    public const string MeterName          = "App.AIHarness";

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

    // --- Extensões da aplicação (fora da spec OTel) ---
    // O prefixo 'app.' é o namespace deste harness. Ao renomeá-lo, atualize
    // TAMBÉM os valores de wire duplicados em deploy/otel/otel-collector-config.yaml
    // e nas queries PromQL do dashboard Grafana — eles não passam por estas
    // constantes e falham em silêncio se saírem de sincronia.
    public const string TenantId       = "app.tenant_id";
    public const string UserId         = "app.user_id";
    public const string CorrelationId  = "app.correlation_id";
    public const string IterationCount = "app.ai.iteration_count";
    public const string IterationIndex = "app.ai.iteration_index";
    public const string CostUsd        = "app.ai.cost_usd";

    // --- Tags de DECISÃO de sampling (contrato com o Collector) ---
    // SEMPRE string. bool serializa como boolValue e não casa com
    // string_attribute no tail_sampling.
    public const string WasFallback = "app.ai.was_fallback";
    public const string Anomalous   = "app.ai.anomalous";
    public const string AuthzDenied = "app.ai.authz_denied";

    // --- Nomes de instrumentos de métrica ---
    // Centralizados aqui porque o AddView() em ObservabilityExtensions precisa
    // casar exatamente com o nome do instrumento; como string solta, uma
    // divergência faz os buckets do histograma voltarem ao default sem erro.
    public const string MetricCost             = "app.ai.cost";
    public const string MetricIterations       = "app.ai.iterations";
    public const string MetricToolInvocations  = "app.ai.tool.invocations";
    public const string MetricAuthzDenials     = "app.ai.authz.denials";
    public const string MetricOperationsActive = "app.ai.operations.active";
}
