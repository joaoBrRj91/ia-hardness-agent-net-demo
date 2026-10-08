# 01b-codebase — Ticket #3 Logs estruturados (somente leitura, sem proposta de solução)

Raiz: C:\Users\joaon\Projetos\project-labs\IA\hardness-ai

## 1. Camadas e arquivos afetados
Domain não precisa mudar. O logging fica em Infrastructure e Api. Projetos da solução (HardnessAI.slnx): Api, Domain, Infrastructure. Não existe projeto de teste.

| Arquivo | Linhas | Relevância |
|---|---|---|
| src\Infrastructure\AI\Harness\AIHarness.cs | 35-132 | `ProcessAsync`. O correlationId nasce na linha 39 e não há `BeginScope`. |
| src\Infrastructure\AI\Observability\InstrumentedAIHarness.cs | 39-108 | Decorator. Já loga Error em 102-104 sem correlationId (usa traceId). |
| src\Infrastructure\AI\Agents\ReActAgent.cs | 91-140, 217-225 | Loop, `StopReason` e circuit breaker. |
| src\Infrastructure\AI\Agents\LoggingAgentObserver.cs | 13-35 | Logs `[ReAct][...]` sem correlationId. |
| src\Infrastructure\AI\Routing\SemanticRouter.cs | 72, 93, 99, 106, 138 | Logs `[Router]` sem correlationId. |
| src\Api\Program.cs | 49-63 | Catches sem log. |
| src\Infrastructure\ObservabilityExtensions.cs | 35, 110-116 | `Decorate` e `IncludeScopes`. |
| src\Infrastructure\DependencyInjection.cs | 101 | Registro de `AIHarness`. |

## 2. Onde o correlationId é gerado e propagado
- Geração em AIHarness.cs:39: `var correlationId = request.CorrelationId ?? Guid.NewGuid().ToString("N")[..16];`
- Entrada: `HarnessRequestDto.CorrelationId` (Program.cs:77) é copiado para `HarnessRequest.CorrelationId` (Program.cs:45). Opcional (`string?`) em src\Domain\AI\Harness\HarnessContracts.cs:21.
- Saída: `HarnessResponse.CorrelationId` (AIHarness.cs:107) e a tag de span `app.correlation_id` (InstrumentedAIHarness.cs:69, constante em GenAiConventions.cs:30).
- O correlationId NÃO é passado a SemanticRouter, ReActAgent, observer nem handlers. Hoje só aparece nos logs do próprio AIHarness: linhas 43, 67-68, 84-85, 117-119 e 128.
- `ToolExecutionContext` (src\Domain\AI\Tools\ToolExecutionContext.cs) só tem `TenantId`, `UserId`, `Roles` e `ReadOnlyMode`. Não tem correlationId.
- `AgentState` tem `TenantId` e `UserId` (ReActAgent.cs:75-80).

## 3. Como o decorator envolve o AIHarness
- DependencyInjection.cs:101: `services.AddScoped<IAIHarness, AIHarness>();`
- ObservabilityExtensions.cs:35: `services.Decorate<IAIHarness, InstrumentedAIHarness>();` (Scrutor 7.0.0, mantém Scoped).
- Program.cs:8-12: `AddAIHarness` precisa vir ANTES de `AddAIObservability`.
- Cadeia do endpoint: `/harness` → `InstrumentedAIHarness.ProcessAsync` → `AIHarness.ProcessAsync` (inner).
- Sem `AddAIObservability`, `IAIHarness` resolve direto para `AIHarness` (vale para testes só com `AddAIHarness`).
- O decorator só vê `request.CorrelationId`, que pode ser nulo.
- Consequência de ordem: o scope aberto em `AIHarness` fica DENTRO do decorator. O log de erro do decorator (102-104) e os catches do Program.cs ficam FORA do scope.

## 4. Padrões atuais de logging
- Templates com prefixo entre colchetes e nomes camelCase: `[Harness] START | correlationId={Id} tenant={Tenant} userId={User} forceIntent={Force}` (AIHarness.cs:42-45).
- Outros prefixos: `[Router]`, `[ReAct][{StepType}]`, `[ReAct][Complete]`, `[Reflection]`, `[Escalate]`, `[RAG]`, `[Registry]`, `[Tool]`, `[MCP]`.
- Não há padrão único de nomes de placeholder. O decorator não segue o padrão: `"Harness failed | tenant={Tenant} traceId={TraceId} elapsed={Ms}ms"`.
- Níveis: Information fluxo normal, Debug detalhes, Warning fallback e circuit breaker do Reflection, Error com exception para falhas. Loggers são `ILogger<T>` injetados.
- Falhas silenciosas no ReActAgent:
  - Circuit breaker em ReActAgent.cs:217-225: `state.WithFinalAnswer(timeout)` + observer, sem log. `MaxIterations = 8` em ReActAgent.cs:31.
  - ReActAgent.cs:140: `if (response.StopReason != "tool_use") break;`. Qualquer `StopReason` que não seja `end_turn` nem `tool_use` (ex.: `max_tokens`) sai do loop sem log e cai no circuit breaker, com a mensagem "não convergiu".
  - Único log do ReAct: Error em 191 (`"Tool dispatch failed: {Tool}"`).
- Precedente para o Warning do circuit breaker: ReflectionAgent.cs:119-121, `_logger.LogWarning("[Reflection] MaxRefinements atingido | tenant={Tenant} bestScore={Score}", ...)`.
- Program.cs:54-63: `catch (InvalidOperationException)` → 500 e `catch (HttpRequestException)` → 502, ambos sem log. A lambda do endpoint não injeta `ILogger`; não há `ILogger` em nenhum arquivo de Api.
  - `AIHarness` e o decorator já logam Error antes do rethrow; o log do Api não existe e o do decorator não tem o correlationId.
- `IncludeScopes = true` confirmado em ObservabilityExtensions.cs:114 (OTLP); `IncludeFormattedMessage = true` na 115.
- appsettings.json: `Logging:LogLevel:Default = Information`. O console padrão NÃO tem `IncludeScopes`, então scopes só aparecem no export OTLP/Loki.

## 5. Testes existentes e convenções
- Nenhum projeto de teste, arquivo `*Test*` ou referência a xUnit. Nenhum `InternalsVisibleTo`.
- CLAUDE.md define xUnit e `dotnet test`. Será preciso criar o projeto e adicioná-lo ao HardnessAI.slnx (pasta `/tests/` ainda inexistente).
- `FakeLLMClient` (src\Infrastructure\AI\LLM\FakeLLMClient.cs), `public sealed`:
  - `Enqueue(params LLMResponse[])`; `LoadScenario(name)` via `FakeScenarios.Get`.
  - Sem fila, `BuildSmartDefault` identifica o papel por `PromptMarkers.Router`/`PromptMarkers.Critic` e devolve `end_turn`.
  - Não lança exceção por padrão. Para 502 (`HttpRequestException`) e `StopReason` inesperado: enfileirar `LLMResponse` com `StopReason` customizado (ex.: `"max_tokens"`) ou criar fake novo.
  - Circuit breaker: enfileirar 8 respostas `tool_use`.
  - `LLMResponse`: `StopReason`, `Content`, `Model`, `InputTokens`, `OutputTokens`, `Text`, `ToolUses` (src\Domain\AI\LLM\LLMContracts.cs).
- DI de teste: `AddAIHarness(config)` com `LLM:UseFake=true` e `AllowedTenants` obrigatório (senão `InvalidOperationException`). `IAgentDiagnostics` tem default NoOp (DependencyInjection.cs:106-110). `TokenUsageAccumulator` é Scoped.
- Infrastructure referencia só `Microsoft.Extensions.Logging.Abstractions`. Capturar logs e scopes exige um `ILogger`/`ILoggerProvider` fake (não existe no repo).
- Testar o 502 do Program.cs exige `WebApplicationFactory` (Microsoft.AspNetCore.Mvc.Testing) e `public partial class Program` (hoje não existe).

## 6. Contratos externos que podem quebrar
- Mensagens de log existentes: Loki/Grafana pode filtrar por texto `[Harness] START` etc. Premissa do ticket: não editá-las.
- Nomes dos atributos de scope (`CorrelationId`, `TenantId`, `UserId`) viram atributos no OTLP. Já existe o span attribute `app.correlation_id` (GenAiConventions.cs:30); convém alinhar o nome. Nomes diferentes entre scope e placeholder duplicam dados.
- HTTP: `POST /harness` retorna 500/502 via `Results.Problem` (Program.cs:57 e 62). O ticket não pede mudar status nem corpo.
- `IAIHarness`, `HarnessRequest`, `HarnessResponse` (src\Domain\AI\Harness\HarnessContracts.cs) são públicos; alterar assinaturas quebra.
- `IAgentObserver` (src\Domain\AI\Agents\ReactContracts.cs) é usado por ReAct e Reflection.

## 7. Riscos
1. Logs fora do scope: o log do `InstrumentedAIHarness` (102-104) e os catches do Program.cs ficam fora do scope aberto em `AIHarness`; o "Harness failed" não leva correlationId.
2. `BeginScope` usa `AsyncLocal` e flui para código awaited dentro de `ProcessAsync`. Deve ser um `using` cobrindo todo o try/catch. Não foi encontrado fire-and-forget.
3. `LoggingAgentObserver` é Singleton (DependencyInjection.cs:86), `ReActAgent` é Scoped. Sem estado por request nos loggers; guardar correlationId em campo do observer vazaria entre requests.
4. ReflectionAgent (107, 119, 237) e AllHandlers (162, `[Escalate]`) logam sem correlationId; o scope do `AIHarness` os cobre, mas chamadas diretas a `SemanticRouter`, `ReActAgent` ou `ReflectionAgent` (testes unitários) não têm scope.
5. Duplicação: com o scope, `correlationId`, `tenant` e `userId` aparecem no scope e nos placeholders de `[Harness] START` etc.
6. Console padrão não mostra scopes; validação local precisa de OTLP ou de um logger fake.
7. O mesmo erro já gera Error em `AIHarness` e no decorator; um terceiro log no Program.cs triplica a exception (ruído aceitável pelo ticket).
8. Os catches do Program.cs só tratam `InvalidOperationException` e `HttpRequestException`. `Router retornou resposta vazia` (SemanticRouter.cs:127) é `InvalidOperationException` e cai no 500 "Configuração inválida".
9. `StopReason` inesperado e circuit breaker terminam no mesmo estado final; logar nos dois pontos gera 2 Warnings no mesmo request.
10. A regra "Toda feature nova começa por um teste que falha" exige criar a infraestrutura de testes primeiro.

## 8. Padrões a copiar
- Warning de circuit breaker: ReflectionAgent.cs:118-121.
- Error com exception e contexto: AIHarness.cs:127-129.
- Prefixo `[Xxx]` e placeholders nomeados.
- Ordem de DI: Program.cs:10-12 e ObservabilityExtensions.cs:19-22.
