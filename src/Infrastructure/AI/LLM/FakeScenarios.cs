using System.Text.Json.Nodes;
using Domain.AI.LLM;

namespace Infrastructure.AI.LLM;

/// <summary>
/// Roteiros determinísticos para o FakeLLMClient (modo offline, sem API key).
///
/// PONTO DE EXTENSÃO: os cenários abaixo são "Hello World" — existem apenas
/// para exercitar os loops ReAct e Reflection de ponta a ponta. Ao adaptar o
/// template, reescreva-os contra as SUAS tools.
///
/// Duas restrições ao reescrever:
///   1. o Name de cada LLMToolUse precisa casar com um IToolDefinition.Name
///      registrado, senão o loop ReAct recebe unknown_tool e não converge;
///   2. o cenário de reflection precisa alternar generator/critic na ordem
///      exata em que o ReflectionAgent consome a fila.
/// </summary>
internal static class FakeScenarios
{
    internal const string ReactToolCall        = "react-tool-call";
    internal const string ReactAuthzDenied     = "react-authz-denied";
    internal const string ReflectionRefinement = "reflection-refinement";

    internal static LLMResponse[] Get(string name) => name switch
    {
        ReactToolCall        => BuildReactToolCall(),
        ReactAuthzDenied     => BuildReactAuthzDenied(),
        ReflectionRefinement => BuildReflectionRefinement(),
        _ => throw new ArgumentException(
                 $"Cenário fake desconhecido: '{name}'. Valores válidos: " +
                 $"'{ReactToolCall}', '{ReactAuthzDenied}', '{ReflectionRefinement}'.",
                 nameof(name))
    };

    // ── ReAct: tool call → final answer ──────────────────────────────────────
    // Use with ForceIntent: "investigate" to bypass the router.
    // R1: agent reasons and calls lookup_item for ITEM-00000003.
    // R2: after the real tool runs (status=active, quantity=108, updatable=true),
    //     agent synthesises the final answer.

    private static LLMResponse[] BuildReactToolCall() =>
    [
        new LLMResponse
        {
            StopReason = "tool_use",
            Content =
            [
                new LLMText(
                    "Preciso verificar o estado atual do item ITEM-00000003 antes de " +
                    "emitir qualquer recomendação. Não devo assumir status nem quantidade " +
                    "sem confirmação direta da fonte."),
                new LLMToolUse(
                    Id:    "toolu_react_lookup_001",
                    Name:  "lookup_item",
                    Input: JsonNode.Parse("""{"item_id":"ITEM-00000003"}"""))
            ]
        },
        new LLMResponse
        {
            StopReason = "end_turn",
            Content =
            [
                new LLMText("""
                    ## Situação do item ITEM-00000003

                    **Situação:** A consulta confirmou que o item ITEM-00000003 está com status
                    *active* e quantidade 108. O campo `updatable` retornou `true`.

                    **Avaliação:** Item ativo e elegível para alteração. Não há bloqueio de
                    status no registro consultado.

                    **Recomendação:** Prosseguir com o ajuste via `update_item`, informando a
                    nova quantidade e um motivo auditável.

                    **Riscos:** `update_item` altera estado e exige justificativa. Em modo
                    somente-leitura a chamada é negada pela ReadOnlyPolicy — confirme o modo
                    de execução antes de prometer a alteração ao solicitante.
                    """)
            ]
        }
    ];

    // ── ReAct: mutating tool blocked by ReadOnlyPolicy ───────────────────────
    // Use with ForceIntent: "investigate" AND readOnlyMode: true.
    // R1: agent calls update_item (mutating). FilterVisible já o esconde do prompt,
    //     mas o fake o chama assim mesmo — que é exatamente o caso que a segunda
    //     camada (Authorize no dispatch) existe para cobrir.
    // R2: agent reads the not_authorized observation and encerra sem alterar estado.
    // A negação também emite app.ai.authz_denied, retido a 100% pelo tail_sampling.

    private static LLMResponse[] BuildReactAuthzDenied() =>
    [
        new LLMResponse
        {
            StopReason = "tool_use",
            Content =
            [
                new LLMText(
                    "O solicitante pediu o ajuste da quantidade do item ITEM-00000003. " +
                    "Vou aplicar a alteração via update_item."),
                new LLMToolUse(
                    Id:    "toolu_react_update_001",
                    Name:  "update_item",
                    Input: JsonNode.Parse(
                        """{"item_id":"ITEM-00000003","quantity":120,"reason":"Ajuste solicitado pelo operador"}"""))
            ]
        },
        new LLMResponse
        {
            StopReason = "end_turn",
            Content =
            [
                new LLMText("""
                    ## Alteração não executada

                    **Situação:** A chamada a `update_item` para ITEM-00000003 foi negada pela
                    camada de autorização — a sessão está em modo somente-leitura.

                    **Avaliação:** Nenhum estado foi alterado. A negação é esperada: `update_item`
                    é marcada como mutadora e a ReadOnlyPolicy bloqueia esse tipo de tool.

                    **Recomendação:** Reexecutar a solicitação fora do modo somente-leitura, com
                    um usuário que tenha permissão de escrita.

                    **Riscos:** Nenhum risco de estado inconsistente — a operação foi bloqueada
                    antes do dispatch.
                    """)
            ]
        }
    ];

    // ── Reflection: inadequate draft → low critic → refined draft → high critic ─
    // Use with ForceIntent: "analyze" to bypass the router.
    // FakeLLMClient ignores the Model field, so generator and critic calls
    // dequeue from the same queue in strict alternation: gen1, critic1, gen2, critic2.

    private static LLMResponse[] BuildReflectionRefinement() =>
    [
        // Generator — draft 1 (intentionally vague: no IDs, no numbers, no specific action)
        new LLMResponse
        {
            StopReason = "end_turn",
            Content =
            [
                new LLMText("""
                    O item parece estar em situação normal. De modo geral não há indícios de
                    problema relevante e a operação pode seguir. Recomendo acompanhar a
                    situação e agir caso algo mude.
                    """)
            ]
        },

        // Critic — rejects draft 1 (score below the 0.80 acceptance threshold)
        new LLMResponse
        {
            StopReason = "end_turn",
            Content =
            [
                new LLMText("""
                    {
                      "score": 0.42,
                      "is_acceptable": false,
                      "issues": [
                        "Nenhum identificador de item é citado — a análise não é rastreável.",
                        "Status e quantidade ausentes: não há evidência concreta sustentando a conclusão.",
                        "A recomendação ('acompanhar a situação') é vaga e não acionável.",
                        "Riscos da recomendação não foram identificados."
                      ],
                      "suggestions": [
                        "Cite o ID do item, o status retornado e a quantidade atual.",
                        "Separe explicitamente o que é fato observado do que é inferência.",
                        "Indique a tool a ser usada e sob quais condições a ação é permitida.",
                        "Liste ao menos um risco concreto da ação recomendada."
                      ],
                      "reasoning": "O draft não cita nenhuma evidência verificável e termina em uma recomendação genérica."
                    }
                    """)
            ]
        },

        // Generator — draft 2 (incorporates every suggestion)
        new LLMResponse
        {
            StopReason = "end_turn",
            Content =
            [
                new LLMText("""
                    ## Análise do item ITEM-00000003

                    **Situação (fatos observados):** O item ITEM-00000003 retornou status *active*
                    com quantidade 108 e `updatable = true` na última consulta via `lookup_item`.

                    **Avaliação (inferência):** Como o status não é *locked*, o item está elegível
                    para alteração. Nada no registro consultado indica bloqueio operacional — mas
                    essa leitura vale apenas para o instante da consulta.

                    **Recomendação:** Executar `update_item` informando a nova quantidade e um
                    motivo auditável. A ação só é permitida fora do modo somente-leitura.

                    **Riscos:** (1) `update_item` muta estado e não tem desfazer automático;
                    (2) em modo somente-leitura a chamada é negada pela ReadOnlyPolicy, então
                    prometer a alteração antes de confirmar o modo gera retrabalho;
                    (3) o status pode mudar entre a consulta e a alteração.
                    """)
            ]
        },

        // Critic — accepts draft 2
        new LLMResponse
        {
            StopReason = "end_turn",
            Content =
            [
                new LLMText("""
                    {
                      "score": 0.91,
                      "is_acceptable": true,
                      "issues": [],
                      "suggestions": [],
                      "reasoning": "O draft cita ID, status e quantidade, separa fato de inferência, indica a tool e a condição de permissão, e enumera riscos concretos."
                    }
                    """)
            ]
        }
    ];
}
