using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Infrastructure.AI.Observability;

/// <summary>
/// Instrumentos de métrica do harness. Regra: nenhuma tag de alta cardinalidade aqui —
/// TenantId/CorrelationId vão em span attribute, nunca em tag de métrica.
/// </summary>
public sealed class AIHarnessMetrics
{
    private readonly Histogram<double>  _operationDuration;
    private readonly Histogram<long>    _tokenUsage;
    private readonly Histogram<double>  _cost;
    private readonly Histogram<int>     _iterations;
    private readonly Counter<long>      _toolInvocations;
    private readonly Counter<long>      _authzDenials;
    private readonly UpDownCounter<int> _activeOperations;

    public AIHarnessMetrics()
    {
        _operationDuration = AIDiagnostics.Meter.CreateHistogram<double>(
            "gen_ai.client.operation.duration", "s", "Duração ponta-a-ponta do harness");

        _tokenUsage = AIDiagnostics.Meter.CreateHistogram<long>(
            "gen_ai.client.token.usage", "{token}", "Tokens por operação");

        _cost = AIDiagnostics.Meter.CreateHistogram<double>(
            "psp.ai.cost", "USD", "Custo estimado por operação");

        _iterations = AIDiagnostics.Meter.CreateHistogram<int>(
            "psp.ai.iterations", "{iteration}", "Iterações do loop ReAct até convergir");

        _toolInvocations = AIDiagnostics.Meter.CreateCounter<long>(
            "psp.ai.tool.invocations", "{call}", "Invocações de tool");

        _authzDenials = AIDiagnostics.Meter.CreateCounter<long>(
            "psp.ai.authz.denials", "{denial}", "Negações da camada de autorização");

        _activeOperations = AIDiagnostics.Meter.CreateUpDownCounter<int>(
            "psp.ai.operations.active", "{operation}", "Operações em voo");
    }

    public void RecordOperation(double seconds, string model, string status, int iterations, double costUsd)
    {
        // Cardinalidade: 3 modelos × ~5 status = 15 séries. Seguro.
        // TenantId NÃO entra aqui (5.000 valores) → vai em span attribute.
        var tags = new TagList
        {
            { GenAiConventions.RequestModel, model },
            { "status", status }
        };

        _operationDuration.Record(seconds, tags);
        _iterations.Record(iterations, tags);
        _cost.Record(costUsd, tags);
    }

    public void RecordTokens(long input, long output, string model)
    {
        _tokenUsage.Record(input,  Tags(model, "input"));
        _tokenUsage.Record(output, Tags(model, "output"));

        static TagList Tags(string model, string type) => new()
        {
            { GenAiConventions.RequestModel, model },
            { "gen_ai.token.type", type }
        };
    }

    public void RecordToolInvocation(string toolName, bool success)
        => _toolInvocations.Add(1, new TagList
        {
            { GenAiConventions.ToolName, toolName },   // poucas tools — baixa cardinalidade, OK
            { "success", success }
        });

    public void RecordAuthzDenial(string toolName)
        => _authzDenials.Add(1, new TagList { { GenAiConventions.ToolName, toolName } });

    /// <summary>
    /// Sobe no início, desce no Dispose. Se o gráfico sobe e não desce,
    /// há chamadas penduradas no provider — timeout mal configurado.
    /// </summary>
    public IDisposable TrackActive(string model)
    {
        var tags = new TagList { { GenAiConventions.RequestModel, model } };
        _activeOperations.Add(1, tags);
        return new ActiveScope(_activeOperations, tags);
    }

    private sealed class ActiveScope(UpDownCounter<int> counter, TagList tags) : IDisposable
    {
        public void Dispose() => counter.Add(-1, tags);
    }
}
