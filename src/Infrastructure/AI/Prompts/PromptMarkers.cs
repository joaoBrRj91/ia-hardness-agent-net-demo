namespace Infrastructure.AI.Prompts;

/// <summary>
/// Marcadores estáveis embutidos nos system prompts.
///
/// Contrato explícito entre os prompts e o FakeLLMClient (modo offline), que
/// precisa identificar QUAL papel está sendo invocado para devolver uma resposta
/// com o formato certo — JSON de roteamento, JSON de crítica, ou texto livre.
///
/// Por que não inspecionar o texto do prompt: sniffing de prosa quebra em
/// silêncio a cada reescrita ou tradução de prompt, sem erro de compilação.
/// Referenciando estas constantes dos dois lados, o acoplamento fica explícito
/// e verificado pelo compilador.
/// </summary>
public static class PromptMarkers
{
    public const string Router = "[[role:router]]";
    public const string Critic = "[[role:critic]]";
}
