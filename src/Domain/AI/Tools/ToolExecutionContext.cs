namespace Domain.AI.Tools;

/// <summary>
/// Contexto de execução propagado por todo o pipeline agentic.
/// Carrega identidade multi-tenant e permissões usadas pelas policies
/// de autorização (Módulo 3.2) e pelos handlers de tool.
/// </summary>
public sealed record ToolExecutionContext
{
    public required string TenantId { get; init; }
    public required string UserId   { get; init; }

    /// <summary>Roles/permissões do principal — usadas por policies de visibilidade.</summary>
    public IReadOnlySet<string> Roles { get; init; } = new HashSet<string>();

    /// <summary>Se true, tools mutadoras (IsMutating) são bloqueadas.</summary>
    public bool ReadOnlyMode { get; init; } = false;

    public static ToolExecutionContext For(
        string tenantId,
        string userId,
        IEnumerable<string>? roles = null,
        bool readOnlyMode = false) => new()
    {
        TenantId     = tenantId,
        UserId       = userId,
        Roles        = new HashSet<string>(roles ?? []),
        ReadOnlyMode = readOnlyMode
    };
}
