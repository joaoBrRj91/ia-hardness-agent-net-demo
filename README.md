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
    AI/Tools/          DemoTools (tools de exemplo) + ToolRegistry
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
        "input": "Investigue o item ITEM-00000003 e diga se posso alterá-lo",
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
  marcado com `app.ai.was_fallback`, `app.ai.anomalous` e `app.ai.authz_denied` — sinais que
  o `tail_sampling` do Collector usa para decidir retenção (100% em erro, latência >20s,
  anomalia semântica ou negação de autorização; 10% de baseline no tráfego saudável).
- **Métricas**: duração ponta-a-ponta, uso de tokens, custo estimado (`AI:Telemetry:Pricing`),
  iterações do ReAct, invocações de tool e negações de autorização (`AIHarnessMetrics`).
- **Logs**: correlacionados automaticamente com `TraceId`/`SpanId`, exportados para o Loki.
- **Redação de dados sensíveis**: `SensitiveDataRedactor` sanitiza PAN (validado por Luhn), CVV
  e segredos/API keys **antes** de qualquer conteúdo entrar na telemetria. Gravar
  prompt/completion como span event é opt-in via `AI:Telemetry:RecordContent` (`false` por
  padrão — **nunca habilite em produção sem revisão de compliance**).
- **Dashboard**: Grafana já vem provisionado com o dashboard `ai-harness`
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

## Adaptando o template para um domínio real

Este repositório é um harness agentic genérico: as tools, a base de conhecimento e os prompts
que acompanham o projeto são um "Hello World" deliberado — existem só para provar que o
pipeline (RAG → Router → ReAct/Reflection → tools → autorização → telemetria) funciona
ponta a ponta, offline e sem API key.

A arquitetura em si não precisa mudar. Para plugar um domínio real, mexa apenas nestes pontos —
todos marcados com `PONTO DE EXTENSÃO` no código:

| # | Arquivo | O que trocar |
|---|---|---|
| 1 | `Infrastructure/AI/Tools/DemoTools.cs` | Substitua pelas suas `IToolDefinition` + `IToolHandler`. Mantenha ao menos uma tool com `IsMutating = true`, senão `ReadOnlyPolicy` e a trilha `app.ai.authz_denied` viram código morto. |
| 2 | `Infrastructure/AI/Tools/DemoToolsRegistration.cs` | Registre as suas tools. É a **única** linha de `AddAIHarness()` acoplada ao domínio. |
| 3 | `Infrastructure/AI/RAG/RagPromptEnricher.cs` | Troque `KnowledgeBase` pelos seus chunks (ou por um vector store) e `EntityIdRegex()` pelo padrão de ID do seu domínio. |
| 4 | Prompts dos agentes | Bloco `DOMÍNIO` em `ReActAgent.cs`, os prompts de generator/critic em `ReflectionAgent.cs` e a lista de intents em `SemanticRouter.cs`. O protocolo ReAct e o schema JSON do critic são estruturais — não mexa neles. |
| 5 | `Infrastructure/AI/LLM/FakeScenarios.cs` | Reescreva os três cenários offline contra as suas tools. O `Name` de cada `LLMToolUse` precisa casar com um `IToolDefinition.Name` registrado. |

Além disso, se quiser um namespace de telemetria próprio, renomeie o prefixo `app.` em
`GenAiConventions.cs` — e lembre de atualizar **junto** os valores duplicados em
`deploy/otel/otel-collector-config.yaml` e nas queries PromQL do dashboard Grafana, que não
passam por essas constantes.

Novos intents não exigem editar enum nenhum: o dispatch é igualdade de string entre
`RouteDecision.Intent` e `IAgentHandler.TargetIntent`. Basta registrar um handler novo com o
seu `TargetIntent` e o seu `ConfidenceThreshold`, e citá-lo no prompt do router.

## Notas

- As tools de demonstração (`echo`, `get_time`, `calculate`, `lookup_item`, `update_item`) e o
  enricher RAG são fixtures determinísticas in-memory — não há backend/vector store real.
- `update_item` é a única tool mutadora: `ReadOnlyPolicy` a bloqueia quando `readOnlyMode=true`,
  e a negação é registrada em `app.ai.authz_denied` para auditoria.
- Modelos usados: `claude-sonnet-4-6` (ReAct/Generator), `claude-haiku-4-5-20251001`
  (Router/Critic/Summarize).
- `ILLMClient` abstrai o provider: `FakeLLMClient` reproduz cenários determinísticos
  (`FakeScenarios.cs`) para testar o pipeline sem chamadas reais; `AnthropicLLMClient` fala
  com a API real. Cenários disponíveis via `LLM:FakeScenario`:

  | Cenário | Como exercitar | O que demonstra |
  |---|---|---|
  | `react-tool-call` | `forceIntent: "investigate"` | Loop ReAct completo: Thought → Action (`lookup_item`) → Observation → resposta final |
  | `react-authz-denied` | `forceIntent: "investigate"` + `readOnlyMode: true` | `ReadOnlyPolicy` bloqueando `update_item` no dispatch; o agente lê a negação e encerra sem mutar estado (tag `app.ai.authz_denied`) |
  | `reflection-refinement` | `forceIntent: "analyze"` | Loop generator/critic: draft vago (score 0.42) → refinado e aceito (score 0.91) |

  Sem cenário na fila, o `FakeLLMClient` responde por papel, identificando router e critic
  pelos marcadores de `PromptMarkers` — o que mantém o roteamento semântico testável offline.
- A stack de observabilidade é opt-in e local/dev-safe: `RecordContent=false` por padrão,
  redação em duas camadas (aplicação + Collector), e todo custo de modelo é zero no modo fake.
