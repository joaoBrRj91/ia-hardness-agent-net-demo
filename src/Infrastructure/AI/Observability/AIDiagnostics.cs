using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Infrastructure.AI.Observability;

/// <summary>
/// ActivitySource e Meter são thread-safe e DEVEM ser estáticos.
/// Motivo: ActivitySource mantém registro GLOBAL de listeners.
/// Instanciar por scope de DI → listeners vazam e AddSource() não captura nada.
/// </summary>
public static class AIDiagnostics
{
    public static readonly ActivitySource Source = new(GenAiConventions.ActivitySourceName, "1.0.0");
    public static readonly Meter          Meter  = new(GenAiConventions.MeterName, "1.0.0");
}
