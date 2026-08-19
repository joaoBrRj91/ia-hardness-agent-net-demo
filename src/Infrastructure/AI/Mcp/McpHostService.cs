using Microsoft.Extensions.Logging;

namespace Infrastructure.AI.Mcp;

/// <summary>
/// Módulo 3.3 — Host de cliente MCP (Model Context Protocol).
/// Placeholder funcional: em produção conecta a servidores MCP externos e
/// projeta suas tools no IToolRegistry. Aqui expõe apenas o ciclo de vida.
/// </summary>
public sealed class McpHostService
{
    private readonly ILogger<McpHostService> _logger;

    public McpHostService(ILogger<McpHostService> logger) => _logger = logger;

    public Task ConnectAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("[MCP] Host inicializado (nenhum servidor MCP configurado).");
        return Task.CompletedTask;
    }

    public IReadOnlyList<string> ConnectedServers => [];
}
