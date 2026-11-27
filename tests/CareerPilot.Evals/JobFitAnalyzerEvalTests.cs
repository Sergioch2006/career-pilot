using System.Runtime.CompilerServices;
using System.Text.Json;
using CareerPilot.Api.Data;
using CareerPilot.Api.DTOs;
using CareerPilot.Api.Models;
using CareerPilot.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CareerPilot.Evals;

public class JobFitAnalyzerEvalTests
{
    [Theory]
    [MemberData(nameof(GetEvaluationScenarios))]
    public async Task AnalyzeAndSaveAsync_ShouldReturnValidSchemaAndCoherentScore(
        string scenarioName,
        string resumeText,
        string jobTitle,
        string company,
        string jobDescription,
        decimal minExpectedScore,
        decimal maxExpectedScore)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var dbOptions = new DbContextOptionsBuilder<CareerPilotDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new CareerPilotDbContext(dbOptions);
        await dbContext.Database.EnsureCreatedAsync();

        dbContext.UserProfiles.Add(new UserProfile
        {
            FullName = $"Candidato {scenarioName}",
            Email = "eval@careerpilot.local",
            SourceType = "json",
            RawText = resumeText
        });
        await dbContext.SaveChangesAsync();

        var stubChatClient = new ResilientChatClient(
            new ScenarioEvalChatClient(scenarioName),
            "EvalPrimary",
            "eval-model-v1",
            null,
            null,
            null,
            NullLogger<ResilientChatClient>.Instance);

        var analyzer = new JobFitAnalyzerService(
            dbContext,
            stubChatClient,
            new NoOpMcpResearchService());

        var result = await analyzer.AnalyzeAndSaveAsync(
            new AnalyzeJobOfferRequest(jobTitle, company, null, jobDescription));

        Assert.NotNull(result.FitScore);
        Assert.InRange(result.FitScore!.Value, minExpectedScore, maxExpectedScore);
        Assert.True(result.FitAnalysis.HasValue, "FitAnalysis JSON deve estar presente.");

        AssertValidJobFitSchema(result.FitAnalysis!.Value);

        var log = await dbContext.AiExecutionLogs.SingleAsync();
        Assert.True(log.Success);
        Assert.Equal("JobFitAnalysis", log.Operation);
        Assert.Equal(result.Id, log.JobOfferId);
        Assert.True(log.TotalTokens > 0);
    }

    [Fact]
    public async Task ResilientChatClient_WhenPrimaryFails_ShouldFallbackToSecondaryProvider()
    {
        var failingPrimary = new FailingChatClient();
        var healthyFallback = new ScenarioEvalChatClient("HighMatch");

        var resilientClient = new ResilientChatClient(
            failingPrimary,
            "Gemini",
            "gemini-3.8-flash",
            healthyFallback,
            "Ollama",
            "llama3.2",
            NullLogger<ResilientChatClient>.Instance);

        var response = await resilientClient.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "Teste fallback")]);

        Assert.NotNull(response.Text);
        Assert.Equal("Ollama", response.AdditionalProperties?[ResilientChatClient.ProviderKey]?.ToString());
        Assert.Equal(true, response.AdditionalProperties?[ResilientChatClient.UsedFallbackKey]);
    }

    [Fact]
    public async Task AnalyzeAndSaveAsync_WhenModelWrapsJsonInProseAndMarkdown_ShouldSanitizeAndSucceed()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var dbOptions = new DbContextOptionsBuilder<CareerPilotDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new CareerPilotDbContext(dbOptions);
        await dbContext.Database.EnsureCreatedAsync();

        dbContext.UserProfiles.Add(new UserProfile
        {
            FullName = "Candidato Resiliente",
            Email = "resiliente@careerpilot.local",
            SourceType = "json",
            RawText = "Senior .NET Engineer with C#, SQL Server, and React."
        });
        await dbContext.SaveChangesAsync();

        var resilientClient = new ResilientChatClient(
            new ScenarioEvalChatClient("HighMatch", wrapWithProse: true),
            "Ollama",
            "llama3.2",
            null,
            null,
            null,
            NullLogger<ResilientChatClient>.Instance);

        var analyzer = new JobFitAnalyzerService(
            dbContext,
            resilientClient,
            new NoOpMcpResearchService());

        var result = await analyzer.AnalyzeAndSaveAsync(
            new AnalyzeJobOfferRequest("Senior .NET Engineer", "FinTech Global", null, "C# and React."));

        Assert.Equal(90m, result.FitScore);
        AssertValidJobFitSchema(result.FitAnalysis!.Value);
    }

    [Fact]
    public async Task GenerateRecruiterReplyAsync_WithEnglishJobAndPtBrAcceptLanguage_ShouldReturnEnglishReplies()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var dbOptions = new DbContextOptionsBuilder<CareerPilotDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new CareerPilotDbContext(dbOptions);
        await dbContext.Database.EnsureCreatedAsync();

        var profile = new UserProfile
        {
            FullName = "Sergio Silva",
            Email = "sergio@careerpilot.local",
            SourceType = "json",
            RawText = "Engenheiro de Software Sênior com 9 anos de experiência em .NET, C# e React."
        };
        dbContext.UserProfiles.Add(profile);
        await dbContext.SaveChangesAsync();

        const string englishDescription =
            "We are hiring a Senior Backend .NET Engineer to build distributed microservices in C#, ASP.NET Core, and PostgreSQL for our global platform.";

        var job = new JobOffer
        {
            UserProfileId = profile.Id,
            Title = "Senior Backend .NET Engineer",
            Company = "NorthStar Cloud",
            Description = englishDescription,
            FitScore = 88m,
            FitAnalysisJson = """{"match_score":88,"justificativa":"Boa aderência técnica."}"""
        };
        dbContext.JobOffers.Add(job);
        await dbContext.SaveChangesAsync();

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.AcceptLanguage = "pt-BR";
        var languageProvider = new RequestLanguageProvider(new HttpContextAccessor { HttpContext = httpContext });
        Assert.False(languageProvider.IsEnglish());

        var replyChatClient = new RecruiterReplyLanguageEvalChatClient();
        var resilientClient = new ResilientChatClient(
            replyChatClient,
            "EvalPrimary",
            "eval-model-v1",
            null,
            null,
            null,
            NullLogger<ResilientChatClient>.Instance);

        var analyzer = new JobFitAnalyzerService(
            dbContext,
            resilientClient,
            new NoOpMcpResearchService(),
            languageProvider);

        var result = await analyzer.GenerateRecruiterReplyAsync(job.Id);

        Assert.NotNull(replyChatClient.CapturedSystemPrompt);
        Assert.Contains("Write the recruiter response strictly in the exact same language as the job description below", replyChatClient.CapturedSystemPrompt);
        Assert.Contains("pt-BR", replyChatClient.CapturedSystemPrompt);
        Assert.NotNull(replyChatClient.CapturedUserPrompt);
        Assert.Contains(englishDescription, replyChatClient.CapturedUserPrompt);

        Assert.True(result.FitAnalysis.HasValue);
        var root = result.FitAnalysis!.Value;
        Assert.True(root.TryGetProperty("resposta_recrutador", out var reply) && reply.ValueKind == JsonValueKind.Object);

        var linkedin = reply.GetProperty("linkedin").GetString() ?? string.Empty;
        var whatsapp = reply.GetProperty("whatsapp").GetString() ?? string.Empty;

        Assert.False(string.IsNullOrWhiteSpace(linkedin));
        Assert.False(string.IsNullOrWhiteSpace(whatsapp));
        Assert.Contains("Hello", linkedin, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("NorthStar Cloud", linkedin);
        Assert.Contains("Hi", whatsapp, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Olá", linkedin, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Olá", whatsapp, StringComparison.OrdinalIgnoreCase);
    }

    public static IEnumerable<object[]> GetEvaluationScenarios()
    {
        yield return
        [
            "HighMatch",
            "Senior Software Engineer with 9 years of experience in .NET 8/10, C#, ASP.NET Core Web API, SQL Server, Entity Framework Core, React, TypeScript, and distributed remote teams.",
            "Senior Full-Stack .NET Engineer",
            "FinTech Global",
            "Looking for a Senior Full-Stack Software Engineer proficient in C#, .NET Core, REST APIs, SQL Server, and React in a remote international team.",
            75m,
            100m
        ];

        yield return
        [
            "MediumMatch",
            "Backend .NET Engineer with 5 years of C#, ASP.NET Core, PostgreSQL, Docker, and basic JavaScript knowledge.",
            "Full-Stack Node.js & AWS Engineer",
            "CloudShop",
            "Seeking a Full-Stack Engineer experienced in Node.js, TypeScript, React, AWS Lambda, DynamoDB, and Terraform.",
            40m,
            80m
        ];

        yield return
        [
            "LowMatch",
            "Junior Frontend Developer with 1 year of experience in HTML, CSS, Tailwind, and React components.",
            "Principal DevOps & Kubernetes Architect",
            "InfraScale",
            "We need a Principal Platform Architect with 10+ years in Kubernetes, Terraform, Go, multi-region AWS networking, and bare-metal Linux.",
            0m,
            45m
        ];
    }

    private static void AssertValidJobFitSchema(JsonElement root)
    {
        Assert.True(root.TryGetProperty("title", out var title) && !string.IsNullOrWhiteSpace(title.GetString()));
        Assert.True(root.TryGetProperty("company", out var company) && !string.IsNullOrWhiteSpace(company.GetString()));
        Assert.True(root.TryGetProperty("match_score", out var score) && score.GetDecimal() >= 0 && score.GetDecimal() <= 100);
        Assert.True(root.TryGetProperty("justificativa", out var just) && !string.IsNullOrWhiteSpace(just.GetString()));

        Assert.True(root.TryGetProperty("requirements", out var req) && req.ValueKind == JsonValueKind.Object);
        Assert.True(req.TryGetProperty("obrigatorios", out var obg) && obg.ValueKind == JsonValueKind.Array);
        Assert.True(req.TryGetProperty("stack", out var stack) && stack.ValueKind == JsonValueKind.Array);

        Assert.True(root.TryGetProperty("pontos_fortes", out var fortes) && fortes.ValueKind == JsonValueKind.Array);
        Assert.True(root.TryGetProperty("gaps_e_estudos", out var gaps) && gaps.ValueKind == JsonValueKind.Array);
        foreach (var gap in gaps.EnumerateArray())
        {
            Assert.True(gap.TryGetProperty("topico", out var topico) && !string.IsNullOrWhiteSpace(topico.GetString()));
            Assert.True(gap.TryGetProperty("prioridade", out var prio) && !string.IsNullOrWhiteSpace(prio.GetString()));
            Assert.True(gap.TryGetProperty("plano_estudo", out var plano) && !string.IsNullOrWhiteSpace(plano.GetString()));
        }

        Assert.True(root.TryGetProperty("dossie_empresa", out var dossie) && dossie.ValueKind == JsonValueKind.Object);
        Assert.True(root.TryGetProperty("resposta_recrutador", out var reply) && reply.ValueKind == JsonValueKind.Object);
        Assert.True(reply.TryGetProperty("linkedin", out var li) && !string.IsNullOrWhiteSpace(li.GetString()));
        Assert.True(reply.TryGetProperty("whatsapp", out var wa) && !string.IsNullOrWhiteSpace(wa.GetString()));
    }

    private sealed class NoOpMcpResearchService : IMcpResearchService
    {
        public Task<McpToolSession> CreateResearchSessionAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new McpToolSession([], []));
    }

    private sealed class FailingChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new HttpRequestException("Simulated primary provider outage");

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    private sealed class ScenarioEvalChatClient : IChatClient
    {
        private readonly string _scenario;
        private readonly bool _wrapWithProse;

        public ScenarioEvalChatClient(string scenario, bool wrapWithProse = false)
        {
            _scenario = scenario;
            _wrapWithProse = wrapWithProse;
        }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var (score, title, company, gapsCount) = _scenario switch
            {
                "HighMatch" => (90, "Senior Full-Stack .NET Engineer", "FinTech Global", 1),
                "MediumMatch" => (58, "Full-Stack Node.js & AWS Engineer", "CloudShop", 2),
                _ => (22, "Principal DevOps & Kubernetes Architect", "InfraScale", 3)
            };

            var gapsList = Enumerable.Range(1, gapsCount).Select(i => new
            {
                topico = $"Tópico de revisão {i} ({_scenario})",
                prioridade = i == 1 ? "alta" : "media",
                plano_estudo = $"Revisar documentação oficial e construir POC prática #{i}."
            });

            var payload = new
            {
                title,
                company,
                requirements = new
                {
                    obrigatorios = new[] { "Experiência sólida na stack principal", "Boas práticas de testes" },
                    desejaveis = new[] { "Experiência em times distribuídos" },
                    stack = new[] { ".NET", "React", "SQL" },
                    senioridade = "Senior",
                    modelo_trabalho = "Remoto",
                    faixa_salarial = "Não informada"
                },
                match_score = score,
                justificativa = $"Avaliação coerente para o cenário {_scenario} com score {score}.",
                pontos_fortes = new[] { "Experiência alinhada aos requisitos centrais avaliados." },
                gaps_e_estudos = gapsList,
                dossie_empresa = new
                {
                    resumo = $"{company} atua com tecnologia em escala.",
                    cultura_e_missao = "Foco em engenharia de qualidade e colaboração remota.",
                    ganchos_why_this_company = new[] { "Escala técnica e cultura de engenharia." }
                },
                vagas_similares_padroes = new[] { "Ênfase em arquitetura resiliente e observabilidade." },
                resposta_recrutador = new
                {
                    linkedin = $"Hello! Thank you for sharing the {title} opportunity at {company}.",
                    whatsapp = $"Hi! Thanks for reaching out about the {title} role at {company}."
                }
            };

            var json = JsonSerializer.Serialize(payload);
            var content = _wrapWithProse
                ? $"Para analisar esta vaga, segue o resultado estruturado:\n```json\n{json}\n```\nEspero que ajude!"
                : json;
            var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, content))
            {
                Usage = new UsageDetails
                {
                    InputTokenCount = 450,
                    OutputTokenCount = 320,
                    TotalTokenCount = 770
                }
            };

            return Task.FromResult(response);
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    private sealed class RecruiterReplyLanguageEvalChatClient : IChatClient
    {
        public string? CapturedSystemPrompt { get; private set; }
        public string? CapturedUserPrompt { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var messageList = messages.ToList();
            CapturedSystemPrompt = messageList.FirstOrDefault(m => m.Role == ChatRole.System)?.Text;
            CapturedUserPrompt = messageList.FirstOrDefault(m => m.Role == ChatRole.User)?.Text;

            var enforcesJobLanguage =
                CapturedSystemPrompt?.Contains("strictly in the exact same language as the job description", StringComparison.OrdinalIgnoreCase) == true;
            var hasEnglishJob =
                CapturedUserPrompt?.Contains("We are hiring a Senior Backend .NET Engineer", StringComparison.Ordinal) == true;

            var payload = enforcesJobLanguage && hasEnglishJob
                ? new
                {
                    linkedin = "Hello! Thank you for reaching out about the Senior Backend .NET Engineer role at NorthStar Cloud. My 9 years of experience with C#, .NET, and distributed systems align closely with your team's needs, and I would be glad to connect.",
                    whatsapp = "Hi! Thanks for sharing the Senior Backend .NET Engineer opportunity at NorthStar Cloud. I have strong experience with C# and ASP.NET Core and would love to chat when convenient."
                }
                : new
                {
                    linkedin = "Olá! Obrigado pelo contato sobre a vaga.",
                    whatsapp = "Oi! Obrigado pela mensagem sobre a vaga."
                };

            var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, JsonSerializer.Serialize(payload)))
            {
                Usage = new UsageDetails
                {
                    InputTokenCount = 220,
                    OutputTokenCount = 110,
                    TotalTokenCount = 330
                }
            };

            return Task.FromResult(response);
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
