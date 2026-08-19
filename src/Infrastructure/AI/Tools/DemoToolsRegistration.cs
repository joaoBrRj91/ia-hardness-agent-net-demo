using Domain.AI.Tools;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.AI.Tools;

/// <summary>
/// PONTO DE EXTENSÃO — registro das tools do domínio.
///
/// AddAIHarness() é deliberadamente livre de domínio: ele chama AddDemoTools()
/// e nada mais sabe sobre quais tools existem. Para plugar um domínio real,
/// troque a chamada em ServiceCollectionExtensions.AddAIHarness por um método
/// equivalente seu — a única linha de wiring que precisa mudar.
///
/// Contrato de lifetime (definido pelo ToolRegistryWithScopedHandlers):
///   IToolDefinition → Singleton (imutável, lido para montar o prompt do LLM)
///   IToolHandler    → Scoped    (pode depender de DbContext/HttpClient por request)
/// </summary>
public static class DemoToolsRegistration
{
    public static IServiceCollection AddDemoTools(this IServiceCollection services)
    {
        services.AddSingleton<IToolDefinition, EchoDefinition>();
        services.AddSingleton<IToolDefinition, GetTimeDefinition>();
        services.AddSingleton<IToolDefinition, CalculateDefinition>();
        services.AddSingleton<IToolDefinition, LookupItemDefinition>();
        services.AddSingleton<IToolDefinition, UpdateItemDefinition>();

        services.AddScoped<IToolHandler, EchoHandler>();
        services.AddScoped<IToolHandler, GetTimeHandler>();
        services.AddScoped<IToolHandler, CalculateHandler>();
        services.AddScoped<IToolHandler, LookupItemHandler>();
        services.AddScoped<IToolHandler, UpdateItemHandler>();

        return services;
    }
}
