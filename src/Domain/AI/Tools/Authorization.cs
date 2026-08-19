namespace Domain.AI.Tools;

/// <summary>Decisão de uma única policy sobre uma tool.</summary>
public sealed record PolicyDecision
{
    public required bool    Allowed { get; init; }
    public          string? Reason  { get; init; }

    public static PolicyDecision Allow()             => new() { Allowed = true };
    public static PolicyDecision Deny(string reason) => new() { Allowed = false, Reason = reason };
}

/// <summary>
/// Módulo 3.2 — política de autorização componível.
/// Múltiplas policies são avaliadas em conjunto (AND lógico).
/// </summary>
public interface IToolPolicy
{
    string Name { get; }

    /// <summary>
    /// Se false, a policy só é avaliada no dispatch (Authorize), não na
    /// filtragem de visibilidade — evita efeitos colaterais (ex: consumir
    /// orçamento de rate limit) apenas para listar tools ao LLM.
    /// </summary>
    bool AppliesToVisibility => true;

    PolicyDecision Evaluate(string toolName, IToolDefinition? definition, ToolExecutionContext context);
}

/// <summary>Resultado agregado da autorização (todas as policies).</summary>
public sealed record AuthorizationResult
{
    public required bool    IsAuthorized { get; init; }
    public          string? DenialReason { get; init; }
    public          string? DeniedBy     { get; init; }

    public static AuthorizationResult Authorized() => new() { IsAuthorized = true };

    public static AuthorizationResult Denied(string policy, string reason) =>
        new() { IsAuthorized = false, DeniedBy = policy, DenialReason = reason };
}

/// <summary>
/// Serviço de autorização — duas camadas:
///   FilterVisible → o LLM só enxerga tools permitidas (menos superfície de ataque).
///   Authorize     → defense-in-depth no momento do dispatch.
/// </summary>
public interface IToolAuthorizationService
{
    IReadOnlyList<IToolDefinition> FilterVisible(
        IEnumerable<IToolDefinition> definitions,
        ToolExecutionContext         context);

    AuthorizationResult Authorize(string toolName, ToolExecutionContext context);
}
