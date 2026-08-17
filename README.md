# hardness-ai — Agentic Design Patterns (.NET 10)

Implementação executável de padrões agentic:
**ReAct**, **Reflection (Generator + Critic)**, **Semantic Routing** e um **Harness** (facade)
que compõe RAG → Router → Agents em um único entry point, sobre a
[Anthropic .NET SDK](https://www.nuget.org/packages/Anthropic.SDK). Inclui uma stack completa
de **observabilidade** (OpenTelemetry → Collector → Jaeger/Prometheus/Loki/Grafana) e um
**`ILLMClient` fake** para rodar e testar o pipeline inteiro offline, sem custo de API.

## Arquitetura

Clean Architecture em 3 projetos:

```
src/
  Domain/           # Contratos puros (records + interfaces), sem dependências de framework
    AI/Agents/        ReactContracts, ReflectionContracts (AgentState, CriticFeedback, ...)
    AI/Routing/        RoutingContracts (RouteDecision, IAgentHandler, ...)
    AI/Harness/        HarnessContracts (HarnessRequest/Response, IAIHarness)
    AI/LLM/            LLMContracts (ILLMClient — abstração provider-agnostic de chat)
    AI/RAG/            IPromptEnricher
    AI/Tools/          ToolExecutionContext, IToolRegistry, IToolPolicy, autorização
  Infrastructure/   # Implementações — depende de Domain + Anthropic.SDK + OpenTelemetry
    AI/Agents/         ReActAgent, ReflectionAgent, LoggingAgentObserver
    AI/Routing/        SemanticRouter + Handlers (Investigate/Analyze/Summarize/Escalate/Fallback)
    AI/Harness/        AIHarness (facade)
    AI/LLM/            AnthropicLLMClient (real) + FakeLLMClient (cenários determinísticos, offline)
    AI/Tools/          Tool definitions/handlers PSP + ToolRegistry
    AI/Authorization/  TenantIsolation / ReadOnly / RateLimit policies
    AI/RAG/            RagPromptEnricher (KB in-memory por keyword)
    AI/Mcp/            McpHostService (placeholder)
    AI/Observability/  Tracing/metrics/logs OTel — ver seção "Observabilidade" abaixo
    DependencyInjection.cs      → AddAIHarness()
    ObservabilityExtensions.cs  → AddAIObservability() (chamado DEPOIS de AddAIHarness)
  Api/              # Minimal API host (ASP.NET Core) + Dockerfile
deploy/             # Configs do stack local: otel-collector, prometheus, loki, grafana
docker-compose.yml  # api + otel-collector + jaeger + prometheus + loki + grafana
```

Tool registry, autorização, RAG e MCP são versões funcionais in-memory que permitem execução
end-to-end sem dependências externas, mais um host **Api** e a stack de observabilidade
descrita abaixo.

### Adaptações à Anthropic.SDK 5.10.0

A spec foi escrita contra uma API mais antiga; ajustes feitos para a versão atual:

| Spec original            | Código atual (SDK 5.10.0)                                        |
|--------------------------|-----------------------------------------------------------------|
| `System = "prompt"`      | `System = [new SystemMessage("prompt")]`                        |
| `TextBlock`              | `TextContent`                                                   |
| `ToolUseBlock`           | `ToolUseContent`                                                |
| `ToolResultBlock`        | `ToolResultContent` (`Content` é `List<ContentBase>`, sem `IsError`) |
| `new Tool { InputSchema }` | `new Common.Tool(new Common.Function(name, desc, jsonSchema))` |
| `new AnthropicClient(key)` | `new AnthropicClient(new APIAuthentication(key))`             |

## Como rodar

Pré-requisitos: **.NET 10 SDK**.

Por padrão (`appsettings.Development.json`), `LLM:UseFake=true` — a API roda **offline**,
sem chave de API, contra o `FakeLLMClient` (cenários determinísticos em `FakeScenarios.cs`).

```bash
# 1. (Opcional) Para usar a API real da Anthropic em vez do fake:
export ANTHROPIC_API_KEY="sk-ant-..."
#    - ou em src/Api/appsettings.json → "Anthropic:ApiKey"
#    - e desative o fake: $env:LLM__UseFake = "false"

# 2. Build
dotnet build HardnessAI.slnx

# 3. Run
dotnet run --project src/Api
```

### Rodando com Docker Compose (API + observabilidade)

```bash
docker compose up -d --build
```

| Serviço     | URL                                | Credenciais |
|-------------|-------------------------------------|-------------|
| API         | http://localhost:8080               | —           |
| Grafana     | http://localhost:3000               | admin/admin |
| Jaeger UI   | http://localhost:16686              | —           |
| Prometheus  | http://localhost:9090               | —           |

Roda com `LLM_USE_FAKE=true` por padrão (offline, sem custo). Copie `.env.example` para
`.env` e defina `LLM_USE_FAKE=false` + `ANTHROPIC_API_KEY` para exercitar chamadas reais
ao modelo com telemetria completa.

### Endpoints

| Método | Rota        | Descrição                                  |
|--------|-------------|--------------------------------------------|
| GET    | `/`         | Metadados do serviço                       |
| GET    | `/health`   | Health check                               |
| POST   | `/harness`  | Entry point agentic (RAG → Router → Agent) |

Exemplo:

```bash
curl -X POST http://localhost:5099/harness \
  -H "Content-Type: application/json" \
  -d '{
        "input": "Investigue o status da transação TXN-ABC12345 e diga se posso estornar",
        "tenantId": "tenant-demo",
        "userId": "analyst-1"
      }'
```

Campos do corpo (`HarnessRequestDto`): `input` (obrigatório), `tenantId`, `userId`,
`roles[]`, `readOnlyMode`, `forceIntent` (`investigate|analyze|summarize|escalate`),
`correlationId`, `skipEnrichment`.

## Observabilidade

`AddAIObservability()` (chamado em `Program.cs` logo após `AddAIHarness()`) instrumenta o
pipeline inteiro com OpenTelemetry — traces, métricas e logs exportados via OTLP/gRPC para
um Collector:

```
api ──OTLP──► otel-collector ──► Jaeger (traces) / Prometheus (metrics) / Loki (logs)
                                   └── Grafana lê os três backends
```

- **Traces**: span raiz `harness.process` (`InstrumentedAIHarness`) + spans internos por
  iteração ReAct, chamada de modelo e execução de tool (`AgentDiagnostics`). Cada trace é
  marcado com `psp.ai.was_fallback`, `psp.ai.anomalous` e `psp.ai.authz_denied` — sinais que
  o `tail_sampling` do Collector usa para decidir retenção (100% em erro, latência >20s,
  anomalia semântica ou negação de autorização; 10% de baseline no tráfego saudável).
- **Métricas**: duração ponta-a-ponta, uso de tokens, custo estimado (`AI:Telemetry:Pricing`),
  iterações do ReAct, invocações de tool e negações de autorização (`AIHarnessMetrics`).
- **Logs**: correlacionados automaticamente com `TraceId`/`SpanId`, exportados para o Loki.
- **Redação de dados sensíveis**: `PaymentDataRedactor` sanitiza PAN (validado por Luhn), CVV
  e segredos/API keys **antes** de qualquer conteúdo entrar na telemetria. Gravar
  prompt/completion como span event é opt-in via `AI:Telemetry:RecordContent` (`false` por
  padrão — **nunca habilite em produção sem revisão de compliance**).
- **Dashboard**: Grafana já vem provisionado com o dashboard `psp-ai-harness`
  (`deploy/grafana/provisioning`).

Suba a stack local com `docker compose up -d --build` (ver seção acima).

## Configuração (`appsettings.json`)

```jsonc
{
  "Anthropic": { "ApiKey": "" },              // vazio → usa ANTHROPIC_API_KEY
  "LLM": { "UseFake": true, "FakeScenario": "reflection-refinement" }, // offline, sem custo
  "AllowedTenants": [ "tenant-demo", "acme" ], // isolamento multi-tenant
  "RateLimit": { "ToolCallsPerMinute": 30 },
  "Otel": { "Endpoint": "http://localhost:4317" },
  "AI": {
    "Telemetry": {
      "RecordContent": false,                 // nunca true em produção sem revisão de compliance
      "MaxContentLength": 2000,
      "DefaultModel": "claude-sonnet-4-6",
      "CostAlertThresholdUsd": 0.50,
      "Pricing": { "claude-sonnet-4-6": { "InputPerMillion": 0.0, "OutputPerMillion": 0.0 } }
    }
  }
}
```

## Notas

- As tools de pagamento (`check_transaction_status`, `issue_refund`) e o enricher RAG são
  fixtures determinísticas in-memory — não há gateway/vector store real.
- `issue_refund` é marcada como mutadora: `ReadOnlyPolicy` a bloqueia quando `readOnlyMode=true`.
- Modelos usados: `claude-sonnet-4-6` (ReAct/Generator), `claude-haiku-4-5-20251001`
  (Router/Critic/Summarize).
- `ILLMClient` abstrai o provider: `FakeLLMClient` reproduz cenários determinísticos
  (`FakeScenarios.cs`, ex.: `react-tool-call`, `reflection-refinement`) para testar o pipeline
  sem chamadas reais; `AnthropicLLMClient` fala com a API real.
- A stack de observabilidade é opt-in e local/dev-safe: `RecordContent=false` por padrão,
  redação em duas camadas (aplicação + Collector), e todo custo de modelo é zero no modo fake.
