using Domain.AI.Tools;
using Microsoft.Extensions.Logging;

namespace Infrastructure.AI.Authorization;

/// <summary>
/// Módulo 3.2 — Compõe todas as IToolPolicy registradas.
/// Autorização por AND lógico: a primeira negação encerra a avaliação.
/// </summary>
public sealed class ToolAuthorizationService : IToolAuthorizationService
{
    private readonly IReadOnlyList<IToolPolicy>     _policies;
    private readonly IReadOnlyDictionary<string, IToolDefinition> _definitions;
    private readonly ILogger<ToolAuthorizationService> _logger;

    public ToolAuthorizationService(
        IEnumerable<IToolPolicy>          policies,
        IEnumerable<IToolDefinition>      definitions,
        ILogger<ToolAuthorizationService> logger)
    {
        _policies    = policies.ToList();
        _definitions = definitions.ToDictionary(d => d.Name);
        _logger      = logger;
    }

    public IReadOnlyList<IToolDefinition> FilterVisible(
        IEnumerable<IToolDefinition> definitions,
        ToolExecutionContext         context)
    {
        var visible = new List<IToolDefinition>();

        foreach (var def in definitions)
        {
            var (PolicyDenied, Decision) = _policies
                .Where(p => p.AppliesToVisibility)
                .Select(p => (PolicyDenied: p, Decision: p.Evaluate(def.Name, def, context)))
                .FirstOrDefault(x => !x.Decision.Allowed);

            if (PolicyDenied is null)
                visible.Add(def);
            else
                _logger.LogDebug(
                    "[Authz] Tool '{Tool}' oculta por {Policy}: {Reason}",
                    def.Name, PolicyDenied.Name, Decision.Reason);
        }

        return visible;
    }

    public AuthorizationResult Authorize(string toolName, ToolExecutionContext context)
    {
        _definitions.TryGetValue(toolName, out var definition);

        foreach (var policy in _policies)
        {
            var decision = policy.Evaluate(toolName, definition, context);
            if (!decision.Allowed)
            {
                _logger.LogWarning(
                    "[Authz] DENY tool='{Tool}' policy={Policy} tenant={Tenant} reason={Reason}",
                    toolName, policy.Name, context.TenantId, decision.Reason);

                return AuthorizationResult.Denied(policy.Name, decision.Reason ?? "Negado.");
            }
        }

        return AuthorizationResult.Authorized();
    }
}
