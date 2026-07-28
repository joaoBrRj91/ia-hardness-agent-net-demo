using System.Collections.Concurrent;
using Domain.AI.Tools;

namespace Infrastructure.AI.Authorization;

/// <summary>
/// Módulo 3.2 — Isola tools por tenant. Se o tenant não está na allowlist,
/// nenhuma tool é autorizada (nem visível).
/// </summary>
public sealed class TenantIsolationPolicy : IToolPolicy
{
    private readonly IReadOnlySet<string> _allowedTenants;

    public TenantIsolationPolicy(IReadOnlySet<string> allowedTenants)
        => _allowedTenants = allowedTenants;

    public string Name => nameof(TenantIsolationPolicy);

    public PolicyDecision Evaluate(string toolName, IToolDefinition? definition, ToolExecutionContext context)
        => _allowedTenants.Contains(context.TenantId)
            ? PolicyDecision.Allow()
            : PolicyDecision.Deny($"Tenant '{context.TenantId}' não autorizado.");
}

/// <summary>
/// Bloqueia tools mutadoras quando o contexto está em ReadOnlyMode.
/// </summary>
public sealed class ReadOnlyPolicy : IToolPolicy
{
    public string Name => nameof(ReadOnlyPolicy);

    public PolicyDecision Evaluate(string toolName, IToolDefinition? definition, ToolExecutionContext context)
    {
        if (context.ReadOnlyMode && definition?.IsMutating == true)
            return PolicyDecision.Deny($"Tool '{toolName}' é mutadora e o contexto é somente-leitura.");

        return PolicyDecision.Allow();
    }
}

/// <summary>
/// Rate limit por (tenant, tool) em janela deslizante de 1 minuto.
/// Singleton — mantém contadores em memória.
/// </summary>
public sealed class RateLimitPolicy : IToolPolicy
{
    private readonly int _maxPerMinute;
    private readonly ConcurrentDictionary<string, Queue<DateTime>> _hits = new();

    public RateLimitPolicy(int maxPerMinute) => _maxPerMinute = maxPerMinute;

    public string Name => nameof(RateLimitPolicy);

    // Rate limit só faz sentido no dispatch — não deve consumir orçamento ao listar tools.
    public bool AppliesToVisibility => false;

    public PolicyDecision Evaluate(string toolName, IToolDefinition? definition, ToolExecutionContext context)
    {
        var key   = $"{context.TenantId}:{toolName}";
        var now   = DateTime.UtcNow;
        var queue = _hits.GetOrAdd(key, _ => new Queue<DateTime>());

        lock (queue)
        {
            while (queue.Count > 0 && now - queue.Peek() > TimeSpan.FromMinutes(1))
                queue.Dequeue();

            if (queue.Count >= _maxPerMinute)
                return PolicyDecision.Deny(
                    $"Rate limit excedido: {_maxPerMinute} chamadas/min para '{toolName}'.");

            queue.Enqueue(now);
            return PolicyDecision.Allow();
        }
    }
}
