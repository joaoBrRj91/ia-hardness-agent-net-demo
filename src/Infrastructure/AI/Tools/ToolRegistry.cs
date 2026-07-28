using System.Text.Json;
using System.Text.Json.Nodes;
using Domain.AI.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Infrastructure.AI.Tools;

/// <summary>
/// Base do registry: mantém as definições (singletons) e expõe ao LLM.
/// O dispatch concreto é implementado pelas subclasses.
/// </summary>
public abstract class ToolRegistry : IToolRegistry
{
    protected readonly IReadOnlyList<IToolDefinition> Definitions;

    protected ToolRegistry(IEnumerable<IToolDefinition> definitions)
        => Definitions = definitions.ToList();

    public IReadOnlyList<IToolDefinition> GetDefinitions() => Definitions;

    public abstract Task<string> DispatchAsync(string toolName, JsonNode? input, CancellationToken ct = default);
}

/// <summary>
/// Registry que resolve os handlers (Scoped) a partir do IServiceProvider
/// a cada dispatch — respeita o lifetime por request.
/// </summary>
public sealed class ToolRegistryWithScopedHandlers : ToolRegistry
{
    private readonly IServiceProvider        _rootProvider;
    private readonly ILogger<ToolRegistry>   _logger;

    public ToolRegistryWithScopedHandlers(
        IEnumerable<IToolDefinition> definitions,
        IServiceProvider             rootProvider,
        ILogger<ToolRegistry>        logger)
        : base(definitions)
    {
        _rootProvider = rootProvider;
        _logger       = logger;
    }

    public override async Task<string> DispatchAsync(
        string            toolName,
        JsonNode?         input,
        CancellationToken ct = default)
    {
        // Handlers são Scoped — cria um escopo próprio para o dispatch.
        await using var scope = _rootProvider.CreateAsyncScope();

        var handler = scope.ServiceProvider
            .GetServices<IToolHandler>()
            .FirstOrDefault(h => h.ToolName == toolName);

        if (handler is null)
        {
            _logger.LogWarning("[Registry] Handler não encontrado para tool '{Tool}'", toolName);
            return JsonSerializer.Serialize(new
            {
                error = "unknown_tool",
                tool  = toolName
            });
        }

        _logger.LogDebug("[Registry] Dispatch → {Tool}", toolName);
        return await handler.HandleAsync(input, ct);
    }
}
