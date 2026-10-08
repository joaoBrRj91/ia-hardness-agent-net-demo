# Ticket — #3: Logs estruturados

Link: https://github.com/joaoBrRj91/ia-hardness-agent-net-demo/issues/3

## Objetivo
`AIHarness` gera `correlationId`, mas `LoggingAgentObserver`, `SemanticRouter` e `ReActAgent` quase nunca o incluem nos logs, e não há `BeginScope`. Além disso há falhas silenciosas: o circuit breaker do ReAct (max 8 iterações) não loga Warning, um `StopReason` inesperado sai do loop sem log, e exceptions de LLM em `Program.cs` viram 502 sem log estruturado.

## Critérios de aceite
O ticket não traz critérios numerados; derivados do texto (a confirmar no plano):
1. `ILogger.BeginScope` aberto no início de `ProcessAsync` (em `AIHarness` ou `InstrumentedAIHarness`) com `CorrelationId`, `TenantId` e `UserId`.
2. Com `IncludeScopes = true` no OTLP logging, esses campos aparecem em todos os logs do request (Loki).
3. Circuit breaker do ReAct (MaxIterations = 8) loga Warning ao ser atingido.
4. `StopReason` inesperado no ReAct loga antes de sair do loop.
5. Exceptions de LLM em `Program.cs` (502) geram log estruturado.

## Restrições
- Hexagonal: Domain não referencia outros projetos; logging fica em Infrastructure/Api.
- Toda feature nova começa por um teste que falha (CLAUDE.md).
- Fora de escopo no texto do ticket: métricas e traces (já existem em `InstrumentedAIHarness`).

## Links e comentários relevantes
- Arquivos citados: `src/Infrastructure/AI/Harness/AIHarness.cs`, `src/Infrastructure/AI/Agents/LoggingAgentObserver.cs`, `src/Infrastructure/AI/Routing/SemanticRouter.cs`, `src/Infrastructure/AI/Agents/ReActAgent.cs`, `src/Api/Program.cs`.
- Sem comentários no ticket.
- Já verificado no código: `IncludeScopes = true` existe em `src/Infrastructure/ObservabilityExtensions.cs:114`.

## Perguntas em aberto
- [ASSUMIDA] Onde abrir o scope: `AIHarness` ou `InstrumentedAIHarness`?
  → premissa: em `AIHarness`, logo após gerar o `correlationId` · base: o decorator `InstrumentedAIHarness` só vê `request.CorrelationId`, que pode ser nulo; o id definitivo nasce no `AIHarness`.
- [ASSUMIDA] Nível dos novos logs.
  → premissa: Warning para circuit breaker e StopReason inesperado; Error (com a exception) para o 502 · base: o ticket pede Warning para o breaker; `ReActAgent` já usa LogError para falha de tool.
- [ASSUMIDA] Com o scope ativo, é preciso repetir `correlationId` nas mensagens existentes?
  → premissa: não; os campos vêm do scope e as mensagens atuais ficam como estão · base: o ticket pede o scope como mecanismo, não a edição de cada mensagem.
- [ASSUMIDA] O catch de `InvalidOperationException` (500) em `Program.cs` também ganha log?
  → premissa: sim, mesmo tratamento do 502 · base: mesma falha silenciosa; o custo é uma linha. Se não for desejado, o plano remove.

## Respostas e decisões
- 2026-10-08 · orquestrador · Ticket lido; nenhuma pergunta bloqueante.
