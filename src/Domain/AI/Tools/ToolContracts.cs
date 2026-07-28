using System.Text.Json.Nodes;

namespace Domain.AI.Tools;

/// <summary>
/// Descreve uma tool para o LLM: nome, descrição e JSON Schema dos inputs.
/// InputSchema é um JsonNode (JSON Schema) — framework-neutral, convertido
/// para o formato do provider na camada de infraestrutura.
/// </summary>
public interface IToolDefinition
{
    string    Name        { get; }
    string    Description { get; }
    JsonNode  InputSchema { get; }

    /// <summary>True se a tool altera estado (ex: emitir estorno). Usada por ReadOnlyPolicy.</summary>
    bool      IsMutating  { get; }
}

/// <summary>
/// Executa uma tool concreta. Um handler por tool, resolvido por nome.
/// </summary>
public interface IToolHandler
{
    string ToolName { get; }

    Task<string> HandleAsync(JsonNode? input, CancellationToken ct = default);
}

/// <summary>
/// Registry de tools — expõe definições ao LLM e faz dispatch por nome.
/// </summary>
public interface IToolRegistry
{
    IReadOnlyList<IToolDefinition> GetDefinitions();

    Task<string> DispatchAsync(string toolName, JsonNode? input, CancellationToken ct = default);
}
