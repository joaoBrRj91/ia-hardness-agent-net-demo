using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace HardnessAI.Tests.Support;

/// <summary>Um log capturado, com o snapshot dos scopes ativos no momento da chamada.</summary>
public sealed record CapturedLog(
    string Category,
    LogLevel Level,
    Exception? Exception,
    string Message,
    IReadOnlyDictionary<string, object?> Properties,
    IReadOnlyDictionary<string, object?> Scope);

/// <summary>
/// Provider de captura para testes. Implementa <see cref="ISupportExternalScope"/>
/// (mesmo mecanismo do exporter OTLP com IncludeScopes) e grava os pares
/// chave/valor de todos os scopes ativos junto de cada log.
/// </summary>
public sealed class CapturingLoggerProvider : ILoggerProvider, ISupportExternalScope
{
    private readonly ConcurrentQueue<CapturedLog> _entries = new();
    private IExternalScopeProvider _scopes = new LoggerExternalScopeProvider();

    public IReadOnlyList<CapturedLog> Entries => _entries.ToArray();

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, this);

    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;

    public void Dispose() { }

    private void Add(CapturedLog entry) => _entries.Enqueue(entry);

    private sealed class CapturingLogger(string category, CapturingLoggerProvider owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
            => owner._scopes.Push(state);

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var properties = new Dictionary<string, object?>();
            if (state is IEnumerable<KeyValuePair<string, object?>> pairs)
                foreach (var (key, value) in pairs)
                    properties[key] = value;

            var scope = new Dictionary<string, object?>();
            owner._scopes.ForEachScope((s, dict) =>
            {
                if (s is IEnumerable<KeyValuePair<string, object?>> scopePairs)
                    foreach (var (key, value) in scopePairs)
                        dict[key] = value;
            }, scope);

            owner.Add(new CapturedLog(
                category, logLevel, exception, formatter(state, exception), properties, scope));
        }
    }
}
