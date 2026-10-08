---
ticket: 3
verdict: ready-for-review   # ready-for-review | needs-answers | needs-split | needs-discussion
complexity: M               # P | M | G
structuralDecision: false
blockingQuestions: 0
assumptions: 8
planWritten: true
---
## Escopo
- Entra:
  - `ILogger.BeginScope` em `AIHarness.ProcessAsync`, logo após gerar o `correlationId`, cobrindo todo o try/catch, com as chaves `app.correlation_id`, `app.tenant_id` e `app.user_id` (constantes de `GenAiConventions`).
  - Helper único de scope em Infrastructure (fonte única dos nomes das chaves), usado por `AIHarness` e por `Program.cs`.
  - Warning no circuit breaker do `ReActAgent` (MaxIterations = 8).
  - Warning para `StopReason` inesperado no `ReActAgent`, sem gerar um segundo Warning do circuit breaker no mesmo request.
  - Log Error com exception nos catches de `Program.cs` (502 `HttpRequestException` e 500 `InvalidOperationException`), dentro de um scope com as mesmas chaves.
  - Criação do projeto de testes xUnit `tests/HardnessAI.Tests` (incluído em `HardnessAI.slnx`), com um `ILoggerProvider` de captura que registra scopes, e `public partial class Program;` para `WebApplicationFactory`.
- Fica fora:
  - Métricas e traces (já existem em `InstrumentedAIHarness`).
  - Editar mensagens de log existentes (`[Harness] START`, `[Router]`, `[ReAct][...]` etc.) ou o log "Harness failed" do decorator.
  - Mudar status HTTP, corpo do `Results.Problem` ou a mensagem "Agente não convergiu" do breaker.
  - Reclassificar "Router retornou resposta vazia" (cai no 500 "Configuração inválida", risco 8 do mapa).
  - `IncludeScopes` no console padrão.

## Impacto
- Domain: nenhum. `HarnessRequest`, `HarnessResponse`, `IAIHarness`, `IAgentObserver` e `ToolExecutionContext` não mudam.
- Application: nenhum (o projeto não existe na solução atual).
- Infrastructure:
  - `src/Infrastructure/AI/Harness/AIHarness.cs`: `using var scope = ...` após a linha 39.
  - Novo helper estático em `src/Infrastructure/AI/Observability/` (ex.: `HarnessLogScope.Begin(ILogger, string? correlationId, string tenantId, string userId)`), que monta um `Dictionary<string, object?>` com as chaves de `GenAiConventions` e omite `app.correlation_id` quando nulo.
  - `src/Infrastructure/AI/Agents/ReActAgent.cs`: Warning em 140 (StopReason inesperado) e em 217-225 (breaker), com flag local para emitir só um.
  - `LoggingAgentObserver` e `SemanticRouter`: sem mudança de código; passam a carregar os campos via scope.
- API:
  - `src/Api/Program.cs`: injeção de `ILogger<Program>` na lambda de `/harness`; nos dois catches, abre o scope (com `dto.CorrelationId` se houver) e loga Error com exception; `public partial class Program;` no fim.
- Testes: novo `tests/HardnessAI.Tests/HardnessAI.Tests.csproj` (xUnit, `Microsoft.AspNetCore.Mvc.Testing`, referências a Api e Infrastructure), adicionado ao `HardnessAI.slnx` numa pasta `/tests/`.

## Riscos
- Risco 1 (logs fora do scope): o scope do `AIHarness` não cobre o log do decorator nem os catches de `Program.cs`. Mitigação: `Program.cs` abre o próprio scope com tenant, user e o `CorrelationId` recebido (se houver); quando o id foi gerado no `AIHarness`, a ligação é pelo `trace_id` (a instrumentação ASP.NET Core cria o span do request e `harness.process` é filho; o OTLP injeta TraceId em todo log), e o `[Harness] ERROR` dentro do scope tem o id gerado e o mesmo `trace_id`. O decorator fica como está.
- Risco 9 (Warning duplicado): StopReason inesperado faz `break` e cai no bloco do breaker. Mitigação: flag local `stoppedUnexpectedly`; o breaker só loga Warning quando o loop esgotou as iterações. Um Warning por request, coberto por teste.
- Nome dos atributos: as chaves de scope viram atributos OTLP. Usar as constantes `app.*` evita ter `CorrelationId` no log e `app.correlation_id` no span. No Loki os pontos viram `_` (`app_correlation_id`). Diverge da redação do ticket ("CorrelationId, TenantId, UserId"), por isso é premissa.
- Duplicação de dados: `[Harness] START/END/ERROR` continuam com `correlationId={Id}` no template além do scope. Aceito pelo ticket (mensagens não mudam).
- Triplicação do Error (AIHarness, decorator, Program.cs) para a mesma exception: ruído aceito.
- `BeginScope` depende de `AsyncLocal`; precisa ser `using var` no início do método para cobrir o catch. Não há fire-and-forget no pipeline.
- `LoggingAgentObserver` é Singleton: nada de guardar correlationId em campo; o scope resolve sem estado.
- Testes com `WebApplicationFactory`: `AddAIObservability` registra exporters OTLP para `localhost:4317`; sem collector a exportação falha em silêncio e não quebra o teste. `AllowedTenants` e `LLM:UseFake=true` precisam ir por configuração em memória.
- Sem contrato externo alterado, sem migration, sem novo serviço ou fila.

## Complexidade: M
Cinco arquivos de produção com mudança pequena (um helper novo, scope em `AIHarness`, dois Warnings no `ReActAgent`, logs em `Program.cs`), mas o repositório não tem infraestrutura de testes: é preciso criar o projeto xUnit, um logger de captura com suporte a scope e o host de teste com `WebApplicationFactory` e um `ILLMClient` que lança exceção. Não é P pelo esforço de infraestrutura de teste e pelo teste de integração da Api; não é G porque não há decisão estrutural nem mudança de contrato e cabe em cinco commits.

## Perguntas em aberto
- [ASSUMIDA] Onde abrir o scope → premissa: em `AIHarness`, logo após gerar o `correlationId` (o decorator só vê `request.CorrelationId`, que pode ser nulo).
- [ASSUMIDA] Nível dos novos logs → premissa: Warning para circuit breaker e StopReason inesperado; Error com exception para 502 e 500.
- [ASSUMIDA] Repetir `correlationId` nas mensagens existentes → premissa: não; os campos vêm do scope.
- [ASSUMIDA] Catch de `InvalidOperationException` (500) também ganha log → premissa: sim, mesmo tratamento do 502.
- [ASSUMIDA] Nomes das chaves do scope → premissa: `app.correlation_id`, `app.tenant_id`, `app.user_id`, as mesmas constantes de `GenAiConventions` usadas nos spans, e não `CorrelationId/TenantId/UserId` como no texto do ticket.
- [ASSUMIDA] Logs de `Program.cs` e do decorator fora do scope do `AIHarness` → premissa: `Program.cs` abre scope próprio com o `CorrelationId` recebido (se houver) e a correlação com o id gerado é feita pelo `trace_id`; o log "Harness failed" do decorator não muda; a geração do id não sai do `AIHarness`.
- [ASSUMIDA] StopReason inesperado seguido do breaker → premissa: só um Warning por request (o de StopReason); o breaker loga apenas quando as 8 iterações se esgotam. A resposta final "não convergiu" continua igual.
- [ASSUMIDA] Estrutura de testes → premissa: um único projeto `tests/HardnessAI.Tests` (xUnit) referenciando Api e Infrastructure, com testes de unidade/integração via DI (`AddAIHarness` + `FakeLLMClient`) e testes de Api via `WebApplicationFactory<Program>`.
