using System.Text.Json.Nodes;
using Domain.AI.LLM;

namespace Infrastructure.AI.LLM;

internal static class FakeScenarios
{
    internal const string ReactToolCall        = "react-tool-call";
    internal const string ReflectionRefinement = "reflection-refinement";

    internal static LLMResponse[] Get(string name) => name switch
    {
        ReactToolCall        => BuildReactToolCall(),
        ReflectionRefinement => BuildReflectionRefinement(),
        _ => throw new ArgumentException(
                 $"Cenário fake desconhecido: '{name}'. " +
                 $"Valores válidos: '{ReactToolCall}', '{ReflectionRefinement}'.",
                 nameof(name))
    };

    // ── ReAct: tool call → final answer ──────────────────────────────────────
    // Use with ForceIntent: "investigate" to bypass the router.
    // R1: agent reasons and calls check_transaction_status for TXN-00123456.
    // R2: after the real tool runs (status=settled, R$54,56, refundable=true),
    //     agent synthesises the final analysis.

    private static LLMResponse[] BuildReactToolCall() =>
    [
        new LLMResponse
        {
            StopReason = "tool_use",
            Content =
            [
                new LLMText(
                    "Preciso verificar o status atual da transação TXN-00123456 antes de " +
                    "emitir qualquer recomendação. Não devo assumir dados sem confirmação " +
                    "direta do gateway de pagamento."),
                new LLMToolUse(
                    Id:    "toolu_react_chk_001",
                    Name:  "check_transaction_status",
                    Input: JsonNode.Parse("""{"transaction_id":"TXN-00123456"}"""))
            ]
        },
        new LLMResponse
        {
            StopReason = "end_turn",
            Content =
            [
                new LLMText("""
                    ## Análise da Transação TXN-00123456

                    **Situação:** A consulta ao gateway confirmou que a transação TXN-00123456
                    está com status *settled*, valor R$ 54,56 (BRL). O campo `refundable`
                    retornou `true`.

                    **Avaliação:** Transação liquidada e elegível para estorno. Não há pendências
                    de autorização nem disputas abertas no registro consultado.

                    **Recomendação:** Prosseguir com o estorno via `issue_refund` informando o
                    motivo operacional. O PSP confirma processamento em até 5 dias úteis após
                    a solicitação.

                    **Riscos:** O estorno é irreversível após confirmação do PSP. Verificar junto
                    ao solicitante se há contra-ordem antes de executar a operação.
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
        // Generator — draft 1 (intentionally vague: no IDs, no amounts, no specific action)
        new LLMResponse
        {
            StopReason = "end_turn",
            Content =
            [
                new LLMText("""
                    ## Análise da Transação

                    A transação parece ter algum problema. Provavelmente foi recusada ou está
                    com status irregular. Recomendo verificar com o suporte do PSP para entender
                    o que aconteceu e tomar a ação adequada conforme necessário.
                    """)
            ]
        },

        // Critic — feedback 1 (score 0.42, not acceptable)
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
                        "Nenhum ID de transação citado — impossível auditar o caso",
                        "Status descrito como 'parece ter algum problema' — inferência sem evidência concreta",
                        "Valor da transação ausente — análise incompleta para o domínio PSP",
                        "Recomendação vaga ('verificar com suporte') — não acionável pelo operador"
                      ],
                      "suggestions": [
                        "Citar o ID da transação (padrão TXN-XXXXXXXX) e o status exato retornado pelo gateway",
                        "Incluir o valor em BRL e a data/hora da transação",
                        "Propor ação específica: estornar via issue_refund, reprocessar ou escalar para N2",
                        "Separar explicitamente o que é fato (dados do gateway) do que é inferência"
                      ],
                      "reasoning": "O draft não cita nenhuma evidência concreta do domínio PSP. A recomendação é genérica e não permite que o operador tome uma ação direta sem pesquisa adicional. Score abaixo do limiar mínimo de 0.80."
                    }
                    """)
            ]
        },

        // Generator — draft 2 (refined: cites IDs, amounts, separates fact from inference)
        new LLMResponse
        {
            StopReason = "end_turn",
            Content =
            [
                new LLMText("""
                    ## Análise da Transação TXN-00123456

                    **Situação:** Conforme dados do gateway consultados, a transação TXN-00123456
                    está com status *settled*, valor R$ 54,56 (BRL). O campo `refundable`
                    retornou `true`, indicando que a janela de estorno está aberta.

                    **Avaliação (fato):** Transação liquidada e dentro da janela de estorno. Não
                    há registro de disputa aberta ou solicitação de chargeback associada ao ID.
                    **(Inferência):** A ausência de contra-ordens sugere que o estorno pode ser
                    processado com baixo risco operacional — hipótese a ser confirmada com o
                    solicitante antes da execução.

                    **Recomendação:** A equipe de operações deve executar `issue_refund` para
                    TXN-00123456 com motivo *"Solicitação de estorno dentro do prazo — cliente
                    confirmou cancelamento"*. Documentar o `refund_id` gerado para rastreabilidade
                    na trilha de auditoria (SLA: processamento em até 5 dias úteis).

                    **Riscos:** (1) O estorno é irreversível após confirmação do PSP — obter
                    aprovação formal do solicitante antes de executar. (2) A janela de estorno
                    pode variar por adquirente — confirmar a regra vigente para o BIN do cartão
                    antes de prosseguir.
                    """)
            ]
        },

        // Critic — feedback 2 (score 0.91, acceptable)
        new LLMResponse
        {
            StopReason = "end_turn",
            Content =
            [
                new LLMText("""
                    {
                      "score": 0.91,
                      "is_acceptable": true,
                      "issues": [
                        "O timestamp poderia ser exato (ISO 8601) em vez de 'consultados' sem data"
                      ],
                      "suggestions": [
                        "Usar o timestamp exato retornado pelo gateway no campo created_at"
                      ],
                      "reasoning": "O draft revisado cita o ID da transação, status, valor e refundable. Distingue claramente fato de inferência. A recomendação identifica equipe responsável, tool a usar, motivo e SLA. Os riscos são concretos e incluem mitigação (aprovação prévia + verificação de regra do adquirente). Pequena imprecisão no timestamp não compromete a qualidade operacional."
                    }
                    """)
            ]
        }
    ];
}
