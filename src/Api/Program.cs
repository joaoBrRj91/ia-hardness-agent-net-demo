using Domain.AI.Harness;
using Domain.AI.Tools;
using Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Módulo 4 — registra todo o pipeline agentic (RAG → Router → Agents → Harness).
builder.Services.AddAIHarness(builder.Configuration);

// Módulo 6.1 — telemetria (OTel). DEPOIS de AddAIHarness: o Decorate de
// IAIHarness exige o registro original já existente.
builder.Services.AddAIObservability(builder.Configuration);

var app = builder.Build();

app.MapGet("/", () => Results.Ok(new
{
    service = "hardness-ai-complete",
    module  = "MOD4 — Agentic Design Patterns",
    endpoints = new[] { "POST /harness", "GET /health" }
}));

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

// Entry point único do harness agentic.
app.MapPost("/harness", async (
    HarnessRequestDto  dto,
    IAIHarness         harness,
    CancellationToken  ct) =>
{
    if (string.IsNullOrWhiteSpace(dto.Input))
        return Results.BadRequest(new { error = "input é obrigatório." });

    var context = ToolExecutionContext.For(
        tenantId:     dto.TenantId,
        userId:       dto.UserId,
        roles:        dto.Roles,
        readOnlyMode: dto.ReadOnlyMode);

    var request = new HarnessRequest
    {
        Input          = dto.Input,
        Context        = context,
        ForceIntent    = dto.ForceIntent,
        CorrelationId  = dto.CorrelationId,
        SkipEnrichment = dto.SkipEnrichment
    };

    try
    {
        var response = await harness.ProcessAsync(request, ct);
        return Results.Ok(response);
    }
    catch (InvalidOperationException ex)
    {
        // Ex: configuração ausente (API key) ou resposta vazia do provider.
        return Results.Problem(title: "Configuração inválida", detail: ex.Message, statusCode: 500);
    }
    catch (HttpRequestException ex)
    {
        // Falha na chamada ao provider LLM (billing, rate limit, indisponibilidade).
        return Results.Problem(title: "Falha no provedor LLM", detail: ex.Message, statusCode: 502);
    }
});

app.Run();

/// <summary>DTO de entrada do endpoint /harness (mapeado para HarnessRequest + contexto).</summary>
public sealed record HarnessRequestDto
{
    public required string   Input          { get; init; }
    public          string   TenantId       { get; init; } = "tenant-demo";
    public          string   UserId         { get; init; } = "user-demo";
    public          string[] Roles          { get; init; } = [];
    public          bool     ReadOnlyMode   { get; init; }
    public          string?  ForceIntent    { get; init; }
    public          string?  CorrelationId  { get; init; }
    public          bool     SkipEnrichment { get; init; }
}
