# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```bash
# Build entire solution
dotnet build HardnessAI.slnx

# Run the API
dotnet run --project src/Api/Api.csproj

# Build a single project
dotnet build src/Infrastructure/Infrastructure.csproj

# Run the full observability stack (API + Collector + Jaeger + Prometheus + Loki + Grafana)
docker compose up -d --build
```

Set `Anthropic:ApiKey` before running against the real Anthropic API (not required when `LLM:UseFake` is `true`):

```bash
# via env var (recommended)
$env:Anthropic__ApiKey = "sk-ant-..."

# or via user-secrets
dotnet user-secrets set "Anthropic:ApiKey" "sk-ant-..." --project src/Api
```

By default (`appsettings.Development.json`), `LLM:UseFake=true` — the API runs fully offline against `FakeLLMClient`, no API key needed. Set `LLM__UseFake=false` (env var) + `Anthropic__ApiKey` to hit the real Anthropic API.

## Architecture

Three-layer Clean Architecture: **Domain → Infrastructure → Api**

```
Domain/         # Contracts only — no dependencies
  AI/Agents/    ReactContracts.cs, ReflectionContracts.cs
  AI/Routing/   RoutingContracts.cs
  AI/Harness/   HarnessContracts.cs (IAIHarness entry point)
  AI/LLM/       LLMContracts.cs (ILLMClient — provider-agnostic chat abstraction)
  AI/Tools/     IToolDefinition, IToolHandler, IToolRegistry
  AI/RAG/       IPromptEnricher

Infrastructure/ # Implementations — depends on Anthropic.SDK + OpenTelemetry
  AI/Agents/    ReActAgent, ReflectionAgent, LoggingAgentObserver
  AI/Routing/   SemanticRouter, Handlers/AllHandlers.cs
  AI/Harness/   AIHarness
  AI/LLM/       AnthropicLLMClient (real), FakeLLMClient (deterministic scenarios, offline)
  AI/Tools/     PaymentTools (PSP fixtures), ToolRegistry
  AI/Authorization/ Policies, ToolAuthorizationService
  AI/RAG/       RagPromptEnricher
  AI/Mcp/       McpHostService
  AI/Observability/ AIDiagnostics, AIHarnessMetrics, AgentDiagnostics, InstrumentedAIHarness,
                     PaymentDataRedactor, TelemetryOptions, GenAiConventions
  DependencyInjection.cs      ← AddAIHarness() wires the agentic pipeline
  ObservabilityExtensions.cs  ← AddAIObservability() wires OpenTelemetry (call AFTER AddAIHarness)

Api/            # Minimal API — single endpoint POST /harness
  Program.cs
  Dockerfile
```

All infrastructure is registered via `AddAIHarness(IConfiguration)`, followed by `AddAIObservability(IConfiguration)` (order matters — see Observability section). The external configuration surface is `appsettings.json`:
- `Anthropic:ApiKey` — required unless `LLM:UseFake` is `true`
- `LLM:UseFake` / `LLM:FakeScenario` — run against `FakeLLMClient` instead of the real API (see `FakeScenarios.cs`)
- `AllowedTenants` — string array, gates TenantIsolationPolicy
- `RateLimit:ToolCallsPerMinute` — default 30
- `Otel:Endpoint` — OTLP gRPC collector endpoint, default `http://localhost:4317`
- `AI:Telemetry:*` — `RecordContent`, `MaxContentLength`, `DefaultModel`, `CostAlertThresholdUsd`, `Pricing` (per-model USD/1M tokens)

## Request Pipeline

`POST /harness` → `AIHarness.ProcessAsync`:
1. **RAG Enrichment** (`IPromptEnricher`) — keyword-based in-memory knowledge base; skip via `SkipEnrichment: true`
2. **Semantic Routing** (`SemanticRouter`) — Haiku classifies intent; bypass via `ForceIntent`
3. **Handler dispatch** — one handler per intent, each has its own `ConfidenceThreshold`; drops to `FallbackHandler` if below threshold

Intent → Handler → Agent mapping:
| Intent | Handler | Agent |
|---|---|---|
| `investigate` | `InvestigateHandler` (threshold 0.75) | `ReActAgent` (Sonnet, max 8 iterations) |
| `analyze` | `AnalyzeHandler` (threshold 0.70) | `ReflectionAgent` (Sonnet generator + Haiku critic, max 3 refinements) |
| `summarize` | `SummarizeHandler` (threshold 0.60) | Single Haiku call |
| `escalate` | `EscalateHandler` (threshold 0.85) | No LLM — generates ESC-* ticket ID |
| _low confidence_ | `FallbackHandler` | No LLM |

## Key Domain Concepts

**AgentState** is immutable; `WithStep()` / `WithFinalAnswer()` return new instances. The ReAct loop appends typed steps (`Thought`, `Action`, `Observation`, `FinalAnswer`) and observers receive each step via `IAgentObserver`.

**Tool authorization** is two-layer: `FilterVisible` hides unauthorized tools from the LLM prompt; `Authorize` re-checks at dispatch time (defense-in-depth). Three policies compose: `TenantIsolationPolicy`, `ReadOnlyPolicy` (blocks `IsMutating` tools), `RateLimitPolicy` (sliding window, in-memory).

**ReflectionAgent** uses two models: Sonnet (generator) and Haiku (critic). The critic must return a strict JSON schema (`CriticFeedback`). On `MaxRefinements` reached, the circuit breaker accepts the last draft unconditionally.

**Tool fixtures** (`PspFixtures`) are deterministic and in-memory — transaction status is derived from the hash of the transaction ID. No external PSP dependency is needed to run end-to-end.

## Observability

`ObservabilityExtensions.AddAIObservability()` wires OpenTelemetry tracing, metrics, and logs, exported via OTLP/gRPC to an `otel-collector` (default `http://localhost:4317`, configurable via `Otel:Endpoint`). It **must be called after `AddAIHarness()`**: it uses Scrutor's `Decorate<IAIHarness, InstrumentedAIHarness>` (requires the original registration to exist) and replaces the default `NoOpAgentDiagnostics` with the real `AgentDiagnostics`.

- **`InstrumentedAIHarness`** — decorator around `IAIHarness` that opens the root `harness.process` span, records RED metrics (rate/errors/duration), token usage, and estimated cost. It tags each trace as `psp.ai.was_fallback` / `psp.ai.anomalous` (fallback handler used, ≥8 ReAct iterations, >100k input tokens, or cost over `AI:Telemetry:CostAlertThresholdUsd`) so the Collector can decide what to retain.
- **`AgentDiagnostics`** — per-iteration/per-model-call/per-tool spans inside `ReActAgent` / `ReflectionAgent`; also tags `psp.ai.authz_denied` for security auditing when a tool call is blocked.
- **`AIHarnessMetrics`** — OTel `Meter` instruments (`gen_ai.client.operation.duration`, `gen_ai.client.token.usage`, `psp.ai.cost`, `psp.ai.iterations`, `psp.ai.tool.invocations`, `psp.ai.authz.denials`, `psp.ai.operations.active`). Only low-cardinality tags (model, status, tool name) go on metrics; `TenantId`/`CorrelationId` go on span attributes only.
- **`PaymentDataRedactor`** (`ITelemetryRedactor`) — Luhn-validated PAN/CVV/secret redaction applied to prompt/completion content *before* it is attached to spans; `AI:Telemetry:RecordContent` is `false` by default and must never be `true` in production without compliance review. The OTel Collector applies a second layer (`attributes/redact` processor — hash/delete on `gen_ai.content.prompt`, `authorization` header, `psp.user_id`).
- **Naming conventions** live in `GenAiConventions` — OTel GenAI semantic-convention attribute names plus PSP-specific extensions (`psp.tenant_id`, `psp.ai.cost_usd`, etc.).
- **Local stack** (`docker-compose.yml`): `api` → `otel-collector` (contrib image, tail-sampling + file-storage) → Jaeger (traces), Prometheus (metrics scrape on `:8889`), Loki (logs); Grafana provisions all three as datasources plus a pre-built `psp-ai-harness` dashboard. The collector's `tail_sampling` processor buffers full traces and retains them on: any error, latency >20s, semantic anomaly, authz denial (100% — audit requirement), or a 10% probabilistic baseline.

Run the stack: `docker compose up -d --build` — API on `:8080`, Grafana on `:3000` (admin/admin), Jaeger UI on `:16686`, Prometheus on `:9090`. Defaults to `LLM_USE_FAKE=true` (offline, no API key/cost); set `LLM_USE_FAKE=false` + `ANTHROPIC_API_KEY` in `.env` to exercise the real model calls end-to-end.

## Models in Use

- Router / Critic / Summarize: `claude-haiku-4-5-20251001`
- ReAct Generator / Reflection Generator: `claude-sonnet-4-6`
