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
```

Set `Anthropic:ApiKey` before running (the API won't start without it):

```bash
# via env var (recommended)
$env:Anthropic__ApiKey = "sk-ant-..."

# or via user-secrets
dotnet user-secrets set "Anthropic:ApiKey" "sk-ant-..." --project src/Api
```

## Architecture

Three-layer Clean Architecture: **Domain → Infrastructure → Api**

```
Domain/         # Contracts only — no dependencies
  AI/Agents/    ReactContracts.cs, ReflectionContracts.cs
  AI/Routing/   RoutingContracts.cs
  AI/Harness/   HarnessContracts.cs (IAIHarness entry point)
  AI/Tools/     IToolDefinition, IToolHandler, IToolRegistry
  AI/RAG/       IPromptEnricher

Infrastructure/ # Implementations — depends on Anthropic.SDK
  AI/Agents/    ReActAgent, ReflectionAgent, LoggingAgentObserver
  AI/Routing/   SemanticRouter, Handlers/AllHandlers.cs
  AI/Harness/   AIHarness
  AI/Tools/     PaymentTools (PSP fixtures), ToolRegistry
  AI/Authorization/ Policies, ToolAuthorizationService
  AI/RAG/       RagPromptEnricher
  AI/Mcp/       McpHostService
  DependencyInjection.cs  ← AddAIHarness() wires the entire pipeline

Api/            # Minimal API — single endpoint POST /harness
  Program.cs
```

All infrastructure is registered via `AddAIHarness(IConfiguration)`. The single external configuration surface is `appsettings.json`:
- `Anthropic:ApiKey` — required
- `AllowedTenants` — string array, gates TenantIsolationPolicy
- `RateLimit:ToolCallsPerMinute` — default 30

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

## Models in Use

- Router / Critic / Summarize: `claude-haiku-4-5-20251001`
- ReAct Generator / Reflection Generator: `claude-sonnet-4-6`
