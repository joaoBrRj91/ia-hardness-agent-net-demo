namespace Infrastructure.AI.Observability;

public interface ITelemetryRedactor
{
    string Redact(string content);
}
