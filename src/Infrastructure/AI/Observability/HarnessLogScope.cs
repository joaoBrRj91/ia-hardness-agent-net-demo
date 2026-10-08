using Microsoft.Extensions.Logging;

namespace Infrastructure.AI.Observability;

/// <summary>
/// Scope de log com correlation, tenant e user. Usa as mesmas chaves dos
/// atributos de span (<see cref="GenAiConventions"/>), para que Tempo e Loki
/// compartilhem os nomes. Com IncludeScopes = true o exporter OTLP anexa os
/// pares a todo log emitido dentro do scope.
/// </summary>
public static class HarnessLogScope
{
    public static IDisposable? Begin(
        ILogger logger,
        string? correlationId,
        string tenantId,
        string userId)
    {
        var state = new Dictionary<string, object?>
        {
            [GenAiConventions.TenantId] = tenantId,
            [GenAiConventions.UserId] = userId
        };

        if (correlationId is not null)
            state[GenAiConventions.CorrelationId] = correlationId;

        return logger.BeginScope(state);
    }
}
