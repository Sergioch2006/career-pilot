using CareerPilot.Api.Data;
using CareerPilot.Api.Extensions;
using CareerPilot.Api.Services;
using CareerPilot.Api.Telemetry;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Data Source=careerpilot.db";

builder.Services.AddDbContext<CareerPilotDbContext>(options =>
    options.UseSqlite(connectionString));

builder.Services.AddHttpClient("McpResearch", client =>
{
    client.Timeout = TimeSpan.FromSeconds(15);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("CareerPilotLocal/1.0");
}).AddStandardResilienceHandler();

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(CareerPilotTelemetry.ServiceName))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddSource(CareerPilotTelemetry.SourceName)
        .AddConsoleExporter())
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddMeter(CareerPilotTelemetry.SourceName));

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IRequestLanguageProvider, RequestLanguageProvider>();
builder.Services.AddAgnosticAi(builder.Configuration);
builder.Services.AddScoped<ITextExtractionService, TextExtractionService>();
builder.Services.AddScoped<IProfileContactExtractor, ProfileContactExtractor>();
builder.Services.AddScoped<IMcpResearchService, McpResearchService>();
builder.Services.AddScoped<IProfileInterviewerService, ProfileInterviewerService>();
builder.Services.AddScoped<IJobFitAnalyzerService, JobFitAnalyzerService>();
builder.Services.AddScoped<IInterviewPdfService, InterviewPdfService>();
builder.Services.AddScoped<IAtsResumeService, AtsResumeService>();
builder.Services.AddSingleton<JobReanalysisQueue>();
builder.Services.AddSingleton<IJobReanalysisQueue>(sp => sp.GetRequiredService<JobReanalysisQueue>());
builder.Services.AddHostedService<JobReanalysisWorker>();
builder.Services.AddControllers();
builder.Services.AddCors(options =>
{
    options.AddPolicy("LocalFrontend", policy =>
    {
        policy.WithOrigins("http://localhost:5173", "http://127.0.0.1:5173")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

var app = builder.Build();

// Garante a criação do banco SQLite local e das tabelas na inicialização.
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<CareerPilotDbContext>();
    dbContext.Database.EnsureCreated();
    await dbContext.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = ON;");
    await EnsureMissingTablesExistAsync(dbContext);
    await SeedProjectMilestonesAsync(dbContext);
}

app.UseCors("LocalFrontend");
app.MapControllers();

app.Run();

static async Task EnsureMissingTablesExistAsync(CareerPilotDbContext dbContext)
{
    await dbContext.Database.ExecuteSqlRawAsync("""
        CREATE TABLE IF NOT EXISTS "AiExecutionLogs" (
            "Id" INTEGER NOT NULL CONSTRAINT "PK_AiExecutionLogs" PRIMARY KEY AUTOINCREMENT,
            "Operation" TEXT NOT NULL,
            "JobOfferId" INTEGER NULL,
            "Provider" TEXT NOT NULL,
            "Model" TEXT NOT NULL,
            "UsedFallback" INTEGER NOT NULL,
            "LatencyMs" INTEGER NOT NULL,
            "InputTokens" INTEGER NOT NULL,
            "OutputTokens" INTEGER NOT NULL,
            "TotalTokens" INTEGER NOT NULL,
            "EstimatedCostUsd" TEXT NOT NULL,
            "Success" INTEGER NOT NULL,
            "ErrorMessage" TEXT NULL,
            "CreatedAt" TEXT NOT NULL
        );
        """);

    await dbContext.Database.ExecuteSqlRawAsync("""
        CREATE TABLE IF NOT EXISTS "ProjectMilestones" (
            "Id" INTEGER NOT NULL CONSTRAINT "PK_ProjectMilestones" PRIMARY KEY AUTOINCREMENT,
            "Title" TEXT NOT NULL,
            "Description" TEXT NOT NULL,
            "Category" TEXT NOT NULL,
            "Tags" TEXT NULL,
            "IsDefault" INTEGER NOT NULL,
            "CreatedAt" TEXT NOT NULL
        );
        """);

    await EnsureJobOffersColumnsExistAsync(dbContext);
}

static async Task EnsureJobOffersColumnsExistAsync(CareerPilotDbContext dbContext)
{
    var connection = dbContext.Database.GetDbConnection();
    if (connection.State != System.Data.ConnectionState.Open)
    {
        await connection.OpenAsync();
    }

    var existingColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    using (var cmd = connection.CreateCommand())
    {
        cmd.CommandText = "PRAGMA table_info('JobOffers');";
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            existingColumns.Add(reader.GetString(1));
        }
    }

    if (!existingColumns.Contains("UserProfileId"))
    {
        await dbContext.Database.ExecuteSqlRawAsync(
            "ALTER TABLE \"JobOffers\" ADD COLUMN \"UserProfileId\" INTEGER NULL;");
    }

    if (!existingColumns.Contains("AnalysisStatus"))
    {
        await dbContext.Database.ExecuteSqlRawAsync(
            "ALTER TABLE \"JobOffers\" ADD COLUMN \"AnalysisStatus\" TEXT NOT NULL DEFAULT 'UpToDate';");
    }
}

static async Task SeedProjectMilestonesAsync(CareerPilotDbContext dbContext)
{
    if (dbContext.ProjectMilestones.Any())
    {
        return;
    }

    dbContext.ProjectMilestones.AddRange(
        new CareerPilot.Api.Models.ProjectMilestone
        {
            Title = "Arquitetura .NET 10 Agnóstica de LLM (Microsoft.Extensions.AI)",
            Description = "Implementação de camada de IA desacoplada usando IChatClient e DelegatingChatClient, permitindo troca transparente entre Gemini, OpenAI e Ollama via configuração.",
            Category = "Arquitetura",
            Tags = ".NET 10, Microsoft.Extensions.AI, IChatClient, Clean Architecture",
            IsDefault = true
        },
        new CareerPilot.Api.Models.ProjectMilestone
        {
            Title = "Orquestração de Servidores MCP (Model Context Protocol)",
            Description = "Integração com SDK oficial ModelContextProtocol em C# conectando servidores stdio/HTTP (Playwright, Brave Search, Filesystem) como AITools para pesquisa autônoma.",
            Category = "IA & Agentes",
            Tags = "MCP, ModelContextProtocol, Playwright, Tool Calling",
            IsDefault = true
        },
        new CareerPilot.Api.Models.ProjectMilestone
        {
            Title = "RAG Local e Persistência Estruturada com SQLite",
            Description = "Armazenamento local-first de currículos, entrevistas adaptativas (ProfileQA) e análises de vagas com Entity Framework Core e SQLite (pronto para SQLite-vec).",
            Category = "Dados",
            Tags = "SQLite, EF Core, RAG Local, SQLite-vec",
            IsDefault = true
        },
        new CareerPilot.Api.Models.ProjectMilestone
        {
            Title = "Resiliência Multi-LLM com Polly (Retry, Circuit Breaker e Fallback)",
            Description = "Políticas de resiliência com Microsoft.Extensions.Http.Resilience e Polly: retry exponencial com jitter, circuit breaker e fallback automático para provedor secundário.",
            Category = "Resiliência",
            Tags = "Polly, Failure Recovery, Circuit Breaker, Multi-LLM",
            IsDefault = true
        },
        new CareerPilot.Api.Models.ProjectMilestone
        {
            Title = "Observabilidade e FinOps de IA com OpenTelemetry",
            Description = "Instrumentação de latência, provedor, tokens de entrada/saída e estimativa de custo em USD via OpenTelemetry e persistência em AiExecutionLogs.",
            Category = "Observabilidade",
            Tags = "OpenTelemetry, FinOps, Métricas, Tracing",
            IsDefault = true
        },
        new CareerPilot.Api.Models.ProjectMilestone
        {
            Title = "Evaluation Gates Automatizados com xUnit",
            Description = "Suíte de testes automatizados (CareerPilot.Evals) validando aderência de schema JSON, coerência de match score e comportamento de fallback.",
            Category = "QA & Evals",
            Tags = "xUnit, Evaluation Gates, Testes Automatizados, Qualidade de IA",
            IsDefault = true
        }
    );

    await dbContext.SaveChangesAsync();
}
