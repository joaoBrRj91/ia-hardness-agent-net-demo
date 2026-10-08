# Plano — 3

## Decisões
- Abrir o scope em `AIHarness.ProcessAsync`, com `using var` logo após a linha 39, cobrindo o try/catch inteiro · descartada: abrir no `InstrumentedAIHarness` · motivo: o decorator só vê `request.CorrelationId` (pode ser nulo); o id definitivo nasce no `AIHarness`.
- Descartada também: mover a geração do `correlationId` para `Program.cs` e abrir um único scope lá · motivo: muda a responsabilidade do `AIHarness` e deixa sem id quem chama `IAIHarness` fora do HTTP; o ticket e o mapa apontam o `AIHarness` como dono do id.
- Chaves do scope = constantes `GenAiConventions.CorrelationId/TenantId/UserId` (`app.correlation_id`, `app.tenant_id`, `app.user_id`) · descartada: `CorrelationId/TenantId/UserId` em PascalCase · motivo: o mesmo dado teria nomes diferentes no span e no log; com as constantes, Tempo e Loki usam o mesmo nome (no Loki vira `app_correlation_id`).
- Helper único `HarnessLogScope.Begin(ILogger logger, string? correlationId, string tenantId, string userId)` em `src/Infrastructure/AI/Observability/`, que devolve o `IDisposable?` de `BeginScope` com um `Dictionary<string, object?>`; omite `app.correlation_id` quando o id é nulo · descartada: montar o dicionário em cada chamador · motivo: uma só fonte para os nomes das chaves, reaproveitada por `AIHarness` e `Program.cs`.
- Risco 1: `Program.cs` abre o próprio scope (via helper) nos catches, com `dto.CorrelationId` se informado, e loga Error com a exception; quando o id foi gerado pelo `AIHarness`, a ligação é pelo `trace_id` (span do ASP.NET Core é pai de `harness.process`; o OTLP injeta TraceId/SpanId em todo log). O log "Harness failed" do decorator não muda · descartada: alterar o decorator para ler o id da resposta ou do scope · motivo: fora do texto do ticket e o decorator já loga traceId.
- Risco 9: flag local `stoppedUnexpectedly` no `ReActAgent`; o Warning de StopReason é emitido antes do `break`, e o Warning do breaker só quando `!state.IsComplete && !stoppedUnexpectedly` · descartada: logar nos dois pontos · motivo: dois Warnings para a mesma causa, e o do breaker ("MaxIterations atingido") seria falso.
- Templates dos novos logs seguem o padrão do repositório (prefixo `[ReAct]`/`[Api]`, placeholders nomeados) e não repetem correlationId/tenant/user quando estão dentro do scope · exemplo de base: ReflectionAgent.cs:119-121.
  - Breaker: `"[ReAct] MaxIterations atingido | maxIterations={MaxIterations} steps={Steps}"`.
  - StopReason: `"[ReAct] StopReason inesperado | stopReason={StopReason} iteration={Iteration}"`.
  - Api 502: `"[Api] Falha no provedor LLM | status={Status}"`; Api 500: `"[Api] Configuração inválida | status={Status}"` (tenant, user e id vêm do scope).
- Testabilidade do `Program.cs`: `public partial class Program;` no fim do arquivo e `WebApplicationFactory<Program>` com configuração em memória (`LLM:UseFake=true`, `AllowedTenants:0=tenant-demo`), `ConfigureTestServices` trocando `ILLMClient` por um fake que lança, e `ConfigureLogging` adicionando o provider de captura · descartada: extrair o handler para uma classe testável isolada · motivo: maior mudança no Api sem ganho para o ticket.
- Captura de logs nos testes: `CapturingLoggerProvider : ILoggerProvider, ISupportExternalScope` (usa `LoggerExternalScopeProvider`), que grava categoria, nível, exception, mensagem, propriedades e o snapshot dos pares chave/valor de todos os scopes ativos · descartada: pacote de terceiros · motivo: pouco código e nenhuma dependência nova além de xUnit e Mvc.Testing.

## Premissas
- Scope em `AIHarness`, não no decorator (ticket, pergunta 1; mapa §3).
- Warning para breaker e StopReason inesperado; Error com exception para 502 e 500 (ticket, pergunta 2; ReflectionAgent.cs:119 e ReActAgent.cs:191).
- Mensagens existentes não mudam; os campos vêm do scope (ticket, pergunta 3; mapa §6 sobre filtros no Loki).
- O catch 500 também loga (ticket, pergunta 4).
- Chaves `app.*` alinhadas com `GenAiConventions` (mapa §6, GenAiConventions.cs:28-30).
- Um Warning por request no ReAct (mapa, risco 9).
- Correlação do `Program.cs` com o id gerado via `trace_id` (ObservabilityExtensions.cs:56 e 108-116).
- Um único projeto de teste `tests/HardnessAI.Tests` referenciando Api e Infrastructure (CLAUDE.md; mapa §5).
- Commits no formato `<tipo>(#3): ...` (CLAUDE.md, Conventional Commits citando o ticket).

## Passos
1. test(#3): criar projeto de testes e infraestrutura de captura de logs
   arquivos: `tests/HardnessAI.Tests/HardnessAI.Tests.csproj` (net10.0, xUnit, `Microsoft.NET.Test.Sdk`, `xunit.runner.visualstudio`, `Microsoft.AspNetCore.Mvc.Testing`, ProjectReference para `src/Api/Api.csproj` e `src/Infrastructure/Infrastructure.csproj`), `tests/HardnessAI.Tests/Support/CapturingLoggerProvider.cs`, `tests/HardnessAI.Tests/Support/HarnessTestHost.cs` (monta `ServiceCollection` com `AddAIHarness` + config em memória + provider de captura), `HardnessAI.slnx` (pasta `/tests/`), `src/Api/Program.cs` (apenas `public partial class Program;`) · teste: `CapturingLoggerProviderTests.Records_active_scope_pairs` (sanidade do provider) e `ProgramSmokeTests.Health_returns_200` via `WebApplicationFactory<Program>`; `dotnet test` verde.

2. feat(#3): abrir scope de log com correlation, tenant e user no AIHarness
   arquivos: `src/Infrastructure/AI/Observability/HarnessLogScope.cs` (novo), `src/Infrastructure/AI/Harness/AIHarness.cs` · testes (escritos antes e vistos falhando): `HarnessLogScopeTests.Uses_GenAiConventions_keys_and_omits_null_correlation`; `AIHarnessLoggingTests.All_logs_carry_generated_correlation_id_in_scope` (request sem `CorrelationId`; todo log das categorias `AIHarness`, `SemanticRouter`, `ReActAgent`, `LoggingAgentObserver` tem `app.correlation_id` igual a `response.CorrelationId`, mais `app.tenant_id` e `app.user_id`); `AIHarnessLoggingTests.Preserves_provided_correlation_id`; `AIHarnessLoggingTests.Error_log_is_inside_scope` (cliente LLM que lança; o `[Harness] ERROR` tem as chaves).

3. feat(#3): logar Warning no circuit breaker do ReActAgent
   arquivos: `src/Infrastructure/AI/Agents/ReActAgent.cs` (bloco 217-225) · teste: `ReActAgentLoggingTests.Logs_warning_when_max_iterations_reached` (`FakeLLMClient` com 8 respostas `tool_use`; exatamente um Warning `[ReAct] MaxIterations atingido`, resposta final continua "Agente não convergiu...").

4. feat(#3): logar Warning para StopReason inesperado sem duplicar o do breaker
   arquivos: `src/Infrastructure/AI/Agents/ReActAgent.cs` (linha 140 e condição do breaker) · testes: `ReActAgentLoggingTests.Logs_single_warning_for_unexpected_stop_reason` (uma resposta `StopReason = "max_tokens"`; exatamente um Warning, com `stopReason=max_tokens`; nenhum Warning de MaxIterations); `ReActAgentLoggingTests.End_turn_logs_no_warning` (regressão).

5. feat(#3): logar Error estruturado nas falhas 502 e 500 do endpoint /harness
   arquivos: `src/Api/Program.cs` (injetar `ILogger<Program>` na lambda; scope via `HarnessLogScope` + `LogError(ex, ...)` nos dois catches) · testes via `WebApplicationFactory<Program>`: `HarnessEndpointLoggingTests.Llm_http_failure_returns_502_and_logs_error` (fake `ILLMClient` lançando `HttpRequestException`, `SkipEnrichment=true`; status 502 inalterado; log Error categoria `Program` com a exception e scope `app.tenant_id`/`app.user_id`/`app.correlation_id` = id enviado); `HarnessEndpointLoggingTests.Invalid_operation_returns_500_and_logs_error` (fake lançando `InvalidOperationException`; 500 e log Error); `HarnessEndpointLoggingTests.Omits_correlation_key_when_not_provided`.

6. Verificação final (sem commit próprio, ou `chore(#3): dotnet format` se o format alterar algo)
   arquivos: nenhum novo · teste: `dotnet build`, `dotnet test`, `dotnet format --verify-no-changes`.

## Critérios de pronto
- 1. `BeginScope` aberto no início de `ProcessAsync` com correlation, tenant e user → `AIHarnessLoggingTests.All_logs_carry_generated_correlation_id_in_scope`, `Preserves_provided_correlation_id`, `HarnessLogScopeTests.Uses_GenAiConventions_keys_and_omits_null_correlation`.
- 2. Com `IncludeScopes = true`, os campos aparecem em todos os logs do request → `AIHarnessLoggingTests.All_logs_carry_generated_correlation_id_in_scope` e `Error_log_is_inside_scope` (via provider com `ISupportExternalScope`, mesmo mecanismo do exporter OTLP); logs do `Program.cs` → `HarnessEndpointLoggingTests.*`. Validação no Loki fica manual (fora do `dotnet test`).
- 3. Circuit breaker loga Warning → `ReActAgentLoggingTests.Logs_warning_when_max_iterations_reached`.
- 4. StopReason inesperado loga antes de sair do loop, um único Warning → `ReActAgentLoggingTests.Logs_single_warning_for_unexpected_stop_reason`.
- 5. Exceptions de LLM em `Program.cs` (502) geram log estruturado; 500 também → `HarnessEndpointLoggingTests.Llm_http_failure_returns_502_and_logs_error`, `Invalid_operation_returns_500_and_logs_error`.
- Status HTTP e mensagens de log existentes inalterados → asserts de status nos testes da Api e ausência de mudanças nos templates existentes no diff.
