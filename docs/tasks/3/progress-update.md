# Progresso — #3

Próxima ação: nenhuma — plano concluído (13/13); aguardando revisão
Escopo original: 13/13 done · Correções de revisão: 0/0 done · Review da PR: 0/0 done

<!--
Status de subtask: pending | red | green | done | blocked
Ids: 1.1, 2.3 (plano) · R1.1 (revisão interna) · P1.1 (review da PR)
Grave este arquivo em red, green e done, nunca só no fim.
-->

## Passo 1 — Projeto de testes e infraestrutura de captura de logs · done
- [x] 1.1 Provider registra scopes ativos junto de cada log · CapturingLoggerProviderTests.Records_active_scope_pairs · done
- [x] 1.2 Api sobe em teste e /health responde 200 (inclui `public partial class Program;`) · ProgramSmokeTests.Health_returns_200 · done

## Passo 2 — Scope de log no AIHarness · done
- [x] 2.1 Helper usa chaves de GenAiConventions e omite correlation nulo · HarnessLogScopeTests.Uses_GenAiConventions_keys_and_omits_null_correlation · done
- [x] 2.2 Todos os logs do request carregam o correlationId gerado, tenant e user · AIHarnessLoggingTests.All_logs_carry_generated_correlation_id_in_scope · done
- [x] 2.3 CorrelationId informado é preservado · AIHarnessLoggingTests.Preserves_provided_correlation_id · done
- [x] 2.4 Log [Harness] ERROR fica dentro do scope · AIHarnessLoggingTests.Error_log_is_inside_scope · done

## Passo 3 — Warning no circuit breaker do ReAct · done
- [x] 3.1 Warning único ao atingir MaxIterations · ReActAgentLoggingTests.Logs_warning_when_max_iterations_reached · done

## Passo 4 — Warning para StopReason inesperado · done
- [x] 4.1 Um único Warning com stopReason, sem Warning de MaxIterations · ReActAgentLoggingTests.Logs_single_warning_for_unexpected_stop_reason · done
- [x] 4.2 end_turn não gera Warning (regressão) · ReActAgentLoggingTests.End_turn_logs_no_warning · done

## Passo 5 — Error estruturado nas falhas 502 e 500 do /harness · done
- [x] 5.1 HttpRequestException → 502 inalterado + log Error com exception e scope · HarnessEndpointLoggingTests.Llm_http_failure_returns_502_and_logs_error · done
- [x] 5.2 InvalidOperationException → 500 inalterado + log Error · HarnessEndpointLoggingTests.Invalid_operation_returns_500_and_logs_error · done
- [x] 5.3 Sem CorrelationId informado, a chave é omitida do scope · HarnessEndpointLoggingTests.Omits_correlation_key_when_not_provided · done

## Passo 6 — Verificação final · done
- [x] 6.1 dotnet build, dotnet test e dotnet format --verify-no-changes verdes · (sem teste novo) · done

---

## Registro
- 2026-10-08 · — · Plano aprovado; branch feat/3-logs-estruturados criada.
- 2026-10-08 · — · 1.2: o teste já passava antes de `public partial class Program;` (Mvc.Testing expõe o Program internal); red não observável, linha adicionada conforme o plano.
- 2026-10-08 · 97d3001 · Passo 1 concluído (1.1, 1.2).
- 2026-10-08 · 4367eda · Passo 2 concluído (2.1-2.4).
- 2026-10-08 · 91384b1 · Passo 3 concluído (3.1). 4.2 é regressão: já passava antes da implementação (red não observável).
- 2026-10-08 · 807a5c3 · Passo 4 concluído (4.1, 4.2).
- 2026-10-08 · 356cda6 · Passo 5 concluído (5.1-5.3).
- 2026-10-08 · — · 6.1: dotnet build 0 erros/0 avisos; dotnet test 12/12 verdes. `dotnet format --verify-no-changes` já falhava no código original da base (alinhamento de colunas em src/, ~559 ocorrências, fora do escopo do ticket); formatados apenas os arquivos novos (tests/ e HarnessLogScope.cs). Pendência: base inteira não é format-clean.
