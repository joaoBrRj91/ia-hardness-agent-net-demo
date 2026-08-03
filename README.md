# hardness-ai — Agentic Design Patterns (.NET 10)

Implementação executável dos padrões agentic descritos em [`complete_code.md`](./complete_code.md):
**ReAct**, **Reflection (Generator + Critic)**, **Semantic Routing** e um **Harness** (facade)
que compõe RAG → Router → Agents em um único entry point, sobre a
[Anthropic .NET SDK](https://www.nuget.org/packages/Anthropic.SDK).

## Arquitetura

Clean Architecture em 3 projetos:

```
src/
  Domain/           # Contratos puros (records + interfaces), sem dependências de framework
    AI/Agents/        ReactContracts, ReflectionContracts (AgentState, CriticFeedback, ...)
    AI/Routing/        RoutingContracts (RouteDecision, IAgentHandler, ...)
    AI/Harness/        HarnessContracts (HarnessRequest/Response, IAIHarness)
    AI/RAG/            IPromptEnricher
    AI/Tools/          ToolExecutionContext, IToolRegistry, IToolPolicy, autorização
  Infrastructure/   # Implementações — depende de Domain + Anthropic.SDK
    AI/Agents/         ReActAgent, ReflectionAgent, LoggingAgentObserver
    AI/Routing/        SemanticRouter + Handlers (Investigate/Analyze/Summarize/Escalate/Fallback)
    AI/Harness/        AIHarness (facade)
    AI/Tools/          Tool definitions/handlers PSP + ToolRegistry
    AI/Authorization/  TenantIsolation / ReadOnly / RateLimit policies
    AI/RAG/            RagPromptEnricher (KB in-memory por keyword)
    AI/Mcp/            McpHostService (placeholder)
    DependencyInjection.cs  → AddAIHarness()
  Api/              # Minimal API host (ASP.NET Core)
```

O `complete_code.md` continha apenas Domain + Infrastructure e referenciava tipos dos
"Módulos 2/3" (tool registry, autorização, RAG, MCP). Estes foram **implementados** aqui
como versões funcionais in-memory para permitir execução end-to-end, mais um host **Api**.

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

```bash
# 1. Configure a chave da Anthropic (uma das opções):
#    - variável de ambiente:
export ANTHROPIC_API_KEY="sk-ant-..."
#    - ou em src/Api/appsettings.json → "Anthropic:ApiKey"

# 2. Build
dotnet build HardnessAI.slnx

# 3. Run
dotnet run --project src/Api
```

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

## Configuração (`appsettings.json`)

```jsonc
{
  "Anthropic": { "ApiKey": "" },              // vazio → usa ANTHROPIC_API_KEY
  "AllowedTenants": [ "tenant-demo", "acme" ], // isolamento multi-tenant
  "RateLimit": { "ToolCallsPerMinute": 30 }
}
```

## Notas

- As tools de pagamento (`check_transaction_status`, `issue_refund`) e o enricher RAG são
  fixtures determinísticas in-memory — não há gateway/vector store real.
- `issue_refund` é marcada como mutadora: `ReadOnlyPolicy` a bloqueia quando `readOnlyMode=true`.
- Modelos usados: `claude-sonnet-4-6` (ReAct/Generator), `claude-haiku-4-5-20251001`
  (Router/Critic/Summarize).
