using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using CareerPilot.Api.Data;
using CareerPilot.Api.DTOs;
using CareerPilot.Api.Helpers;
using CareerPilot.Api.Models;
using CareerPilot.Api.Telemetry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;

namespace CareerPilot.Api.Services;

public interface IJobFitAnalyzerService
{
    Task<JobOfferAnalysisResponse> AnalyzeAndSaveAsync(
        AnalyzeJobOfferRequest request,
        CancellationToken cancellationToken = default);

    Task<JobOfferAnalysisResponse> GenerateRecruiterReplyAsync(
        int jobId,
        CancellationToken cancellationToken = default);

    Task ReanalyzeExistingAsync(
        int jobId,
        int profileId,
        CancellationToken cancellationToken = default);
}

public class JobFitAnalyzerService : IJobFitAnalyzerService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly CareerPilotDbContext _dbContext;
    private readonly IChatClient _chatClient;
    private readonly IMcpResearchService _mcpResearchService;
    private readonly IRequestLanguageProvider? _languageProvider;

    public JobFitAnalyzerService(
        CareerPilotDbContext dbContext,
        IChatClient chatClient,
        IMcpResearchService mcpResearchService,
        IRequestLanguageProvider? languageProvider = null)
    {
        _dbContext = dbContext;
        _chatClient = chatClient;
        _mcpResearchService = mcpResearchService;
        _languageProvider = languageProvider;
    }

    public async Task<JobOfferAnalysisResponse> AnalyzeAndSaveAsync(
        AnalyzeJobOfferRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Description) && string.IsNullOrWhiteSpace(request.Url))
        {
            throw new ArgumentException("Informe a descrição da vaga ou uma URL válida.");
        }

        var profile = request.ProfileId.HasValue
            ? await _dbContext.UserProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.Id == request.ProfileId.Value, cancellationToken)
            : await _dbContext.UserProfiles.AsNoTracking().OrderByDescending(p => p.Id).FirstOrDefaultAsync(cancellationToken);

        if (profile is null)
        {
            throw new InvalidOperationException("Nenhum perfil encontrado. Importe seu currículo antes de analisar uma vaga.");
        }

        var qaList = await _dbContext.ProfileQAs
            .AsNoTracking()
            .Where(q => q.UserProfileId == profile.Id && q.Answer != null)
            .OrderBy(q => q.Id)
            .Select(q => $"- [{q.Category ?? "geral"}] {q.Question} => {q.Answer}")
            .ToListAsync(cancellationToken);

        var qaSection = qaList.Count == 0
            ? "Nenhuma resposta adicional registrada em ProfileQA."
            : string.Join("\n", qaList);

        await using var mcpSession = await _mcpResearchService.CreateResearchSessionAsync(cancellationToken);

        var isEnglish = _languageProvider?.IsEnglish() ?? false;
        var analysisLanguage = isEnglish ? "ENGLISH (en-US)" : "PORTUGUESE (pt-BR)";

        var systemPrompt = $$"""
            Você é um especialista em recrutamento técnico seguindo a skill job-fit-analyzer.
            Passos obrigatórios:
            1. Leia a descrição da vaga (e se houver URL da vaga sem descrição completa, use a ferramenta de fetch para lê-la).
            2. Extraia: requisitos obrigatórios, desejáveis, stack, senioridade, modelo de trabalho e faixa salarial (se houver).
            3. Use as ferramentas MCP disponíveis (Fetch e Search) para:
               - Pesquisar o site oficial da empresa: missão, valores, produtos principais e tempo de mercado.
               - Pesquisar 3 a 5 vagas similares (mesmo cargo/stack) para identificar padrões de mercado e requisitos ocultos.
            4. Compare com o Perfil + ProfileQA do candidato e retorne EXCLUSIVAMENTE um JSON válido com esta estrutura.
            IDIOMA DA ANÁLISE: Escreva "justificativa", "pontos_fortes", "gaps_e_estudos", "dossie_empresa", "vagas_similares_padroes" e "requirements" no idioma {{analysisLanguage}}.
            {
              "title": "Título da vaga",
              "company": "Nome da empresa",
              "requirements": {
                "obrigatorios": ["..."],
                "desejaveis": ["..."],
                "stack": ["..."],
                "senioridade": "...",
                "modelo_trabalho": "...",
                "faixa_salarial": "..."
              },
              "match_score": 85,
              "justificativa": "...",
              "pontos_fortes": ["..."],
              "gaps_e_estudos": [
                { "topico": "...", "prioridade": "alta|media|baixa", "plano_estudo": "..." }
              ],
              "dossie_empresa": {
                "resumo": "...",
                "cultura_e_missao": "...",
                "ganchos_why_this_company": ["..."]
              },
              "vagas_similares_padroes": ["..."],
              "resposta_recrutador": {
                "linkedin": "Mensagem profissional e objetiva para responder no chat do LinkedIn",
                "whatsapp": "Mensagem profissional, cordial e mais direta para responder no WhatsApp"
              }
            }
            REGRA DE IDIOMA PARA "resposta_recrutador":
            - Se a descrição/mensagem da vaga estiver em INGLÊS, escreva "linkedin" e "whatsapp" em INGLÊS.
            - Se a descrição/mensagem da vaga estiver em PORTUGUÊS, escreva "linkedin" e "whatsapp" em PORTUGUÊS.
            - Use tom profissional, educado e natural de chat, citando brevemente aderência com a vaga e disponibilidade para conversar.
            """;

        var userPrompt = $"""
            ## Dados da Vaga
            Título informado: {request.Title ?? "(extrair do conteúdo)"}
            Empresa informada: {request.Company ?? "(extrair do conteúdo)"}
            URL da vaga: {request.Url ?? "(não informada)"}
            Descrição da vaga:
            {request.Description ?? "(buscar via URL)"}

            ## Perfil do Candidato
            {profile.RawText}

            ## Respostas Complementares (ProfileQA)
            {qaSection}
            """;

        var chatOptions = new ChatOptions
        {
            Tools = mcpSession.Tools
        };

        var sw = Stopwatch.StartNew();
        ChatResponse response;
        try
        {
            try
            {
                response = await _chatClient.GetResponseAsync(
                    [
                        new ChatMessage(ChatRole.System, systemPrompt),
                        new ChatMessage(ChatRole.User, userPrompt)
                    ],
                    chatOptions,
                    cancellationToken);
            }
            catch (Exception) when (mcpSession.Tools.Count > 0)
            {
                // Fallback para provedores/modelos que exigem thoughtSignature em multi-turn tool calling:
                // executa as ferramentas MCP/Fetch/Search diretamente e injeta o contexto pesquisado no prompt.
                var researchContext = await RunDirectResearchAsync(
                    mcpSession.Tools,
                    request,
                    cancellationToken);

                var enrichedUserPrompt = $"""
                    {userPrompt}

                    ## Dados Pesquisados via Ferramentas (Fetch & Search)
                    {researchContext}
                    """;

                response = await _chatClient.GetResponseAsync(
                    [
                        new ChatMessage(ChatRole.System, systemPrompt),
                        new ChatMessage(ChatRole.User, enrichedUserPrompt)
                    ],
                    cancellationToken: cancellationToken);
            }
        }
        catch (Exception ex)
        {
            sw.Stop();
            await SaveExecutionLogAsync(null, null, sw.ElapsedMilliseconds, false, ex.Message, cancellationToken);
            throw;
        }

        sw.Stop();

        var cleanedJson = JsonCleanupHelper.StripMarkdownCodeFence(response.Text ?? string.Empty);
        var parsed = JsonSerializer.Deserialize<JobAnalysisAiPayload>(cleanedJson, JsonOptions)
            ?? throw new InvalidOperationException("A IA não retornou uma análise de vaga em JSON válido.");

        var resolvedTitle = !string.IsNullOrWhiteSpace(request.Title)
            ? request.Title.Trim()
            : (!string.IsNullOrWhiteSpace(parsed.Title) ? parsed.Title.Trim() : "Vaga sem título");

        var resolvedCompany = !string.IsNullOrWhiteSpace(request.Company)
            ? request.Company.Trim()
            : (!string.IsNullOrWhiteSpace(parsed.Company) ? parsed.Company.Trim() : "Empresa não identificada");

        var jobOffer = new JobOffer
        {
            UserProfileId = profile.Id,
            Title = resolvedTitle,
            Company = resolvedCompany,
            Url = string.IsNullOrWhiteSpace(request.Url) ? null : request.Url.Trim(),
            Description = string.IsNullOrWhiteSpace(request.Description)
                ? $"Importada via URL: {request.Url}"
                : request.Description.Trim(),
            RequirementsJson = parsed.Requirements?.GetRawText(),
            FitScore = parsed.MatchScore,
            FitAnalysisJson = cleanedJson,
            AnalysisStatus = "UpToDate",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _dbContext.JobOffers.Add(jobOffer);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await SaveExecutionLogAsync(jobOffer.Id, response, sw.ElapsedMilliseconds, true, null, cancellationToken);

        using var analysisDoc = JsonDocument.Parse(cleanedJson);
        return new JobOfferAnalysisResponse(
            jobOffer.Id,
            jobOffer.Title,
            jobOffer.Company,
            jobOffer.Url,
            jobOffer.Description,
            jobOffer.FitScore,
            jobOffer.AnalysisStatus,
            jobOffer.UserProfileId,
            parsed.Requirements,
            analysisDoc.RootElement.Clone(),
            jobOffer.CreatedAt);
    }

    public async Task ReanalyzeExistingAsync(
        int jobId,
        int profileId,
        CancellationToken cancellationToken = default)
    {
        var job = await _dbContext.JobOffers.FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken);
        var profile = await _dbContext.UserProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.Id == profileId, cancellationToken);
        if (job is null || profile is null)
        {
            return;
        }

        var qaList = await _dbContext.ProfileQAs
            .AsNoTracking()
            .Where(q => q.UserProfileId == profile.Id && q.Answer != null)
            .OrderBy(q => q.Id)
            .Select(q => $"- [{q.Category ?? "geral"}] {q.Question} => {q.Answer}")
            .ToListAsync(cancellationToken);

        var qaSection = qaList.Count == 0
            ? "Nenhuma resposta adicional registrada em ProfileQA."
            : string.Join("\n", qaList);

        var sw = Stopwatch.StartNew();
        var systemPrompt = """
            Você é um especialista em recrutamento técnico seguindo a skill job-fit-analyzer.
            Reavalie a compatibilidade entre a vaga abaixo e o currículo atualizado do candidato.
            Retorne EXCLUSIVAMENTE um JSON válido com a estrutura:
            {
              "title": "...",
              "company": "...",
              "requirements": {
                "obrigatorios": ["..."],
                "desejaveis": ["..."],
                "stack": ["..."],
                "senioridade": "...",
                "modelo_trabalho": "...",
                "faixa_salarial": "..."
              },
              "match_score": 85,
              "justificativa": "...",
              "pontos_fortes": ["..."],
              "gaps_e_estudos": [
                { "topico": "...", "prioridade": "alta|media|baixa", "plano_estudo": "..." }
              ],
              "dossie_empresa": {
                "resumo": "...",
                "cultura_e_missao": "...",
                "ganchos_why_this_company": ["..."]
              },
              "vagas_similares_padroes": ["..."],
              "resposta_recrutador": {
                "linkedin": "...",
                "whatsapp": "..."
              }
            }
            """;

        var userPrompt = $"""
            ## Vaga
            Cargo: {job.Title}
            Empresa: {job.Company}
            Descrição: {job.Description}

            ## Currículo Atualizado do Candidato
            {profile.RawText}

            ## Respostas Complementares (ProfileQA)
            {qaSection}
            """;

        var response = await _chatClient.GetResponseAsync(
            [
                new ChatMessage(ChatRole.System, systemPrompt),
                new ChatMessage(ChatRole.User, userPrompt)
            ],
            cancellationToken: cancellationToken);

        sw.Stop();

        var cleanedJson = JsonCleanupHelper.StripMarkdownCodeFence(response.Text ?? string.Empty);
        var parsed = JsonSerializer.Deserialize<JobAnalysisAiPayload>(cleanedJson, JsonOptions)
            ?? throw new InvalidOperationException("A IA não retornou JSON válido na reanálise.");

        job.UserProfileId = profile.Id;
        job.FitScore = parsed.MatchScore;
        job.RequirementsJson = parsed.Requirements?.GetRawText() ?? job.RequirementsJson;
        job.FitAnalysisJson = cleanedJson;
        job.AnalysisStatus = "UpToDate";
        job.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
        await SaveExecutionLogAsync(job.Id, response, sw.ElapsedMilliseconds, true, null, cancellationToken);
    }

    public async Task<JobOfferAnalysisResponse> GenerateRecruiterReplyAsync(
        int jobId,
        CancellationToken cancellationToken = default)
    {
        var job = await _dbContext.JobOffers.FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken)
            ?? throw new KeyNotFoundException($"Vaga {jobId} não encontrada.");

        var profile = await _dbContext.UserProfiles
            .AsNoTracking()
            .OrderByDescending(p => p.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var qaList = profile is null
            ? []
            : await _dbContext.ProfileQAs
                .AsNoTracking()
                .Where(q => q.UserProfileId == profile.Id && q.Answer != null)
                .OrderBy(q => q.Id)
                .Select(q => $"- [{q.Category ?? "geral"}] {q.Question} => {q.Answer}")
                .ToListAsync(cancellationToken);

        var systemPrompt = """
            Você é um assistente de carreira que redige respostas profissionais para recrutadores no LinkedIn e WhatsApp.
            REGRA OBRIGATÓRIA DE IDIOMA:
            - Identifique o idioma da descrição/mensagem da vaga recebida.
            - Se estiver em INGLÊS, escreva ambas as respostas em INGLÊS.
            - Se estiver em PORTUGUÊS, escreva ambas as respostas em PORTUGUÊS.
            Responda EXCLUSIVAMENTE com JSON válido:
            {
              "linkedin": "Mensagem profissional e objetiva para responder no chat do LinkedIn",
              "whatsapp": "Mensagem profissional, cordial e mais direta para responder no WhatsApp"
            }
            """;

        var userPrompt = $"""
            ## Vaga / Mensagem Recebida
            Cargo: {job.Title}
            Empresa: {job.Company}
            Descrição: {job.Description}

            ## Candidato
            Nome: {profile?.FullName ?? "Candidato"}
            Currículo: {profile?.RawText ?? "(não informado)"}
            Respostas complementares: {(qaList.Count == 0 ? "(nenhuma)" : string.Join("\n", qaList))}
            """;

        var response = await _chatClient.GetResponseAsync(
            [
                new ChatMessage(ChatRole.System, systemPrompt),
                new ChatMessage(ChatRole.User, userPrompt)
            ],
            cancellationToken: cancellationToken);

        var cleanedReplyJson = JsonCleanupHelper.StripMarkdownCodeFence(response.Text ?? string.Empty);
        using var replyDoc = JsonDocument.Parse(cleanedReplyJson);

        var mergedAnalysis = MergeRecruiterReplyIntoAnalysis(job.FitAnalysisJson, replyDoc.RootElement);
        job.FitAnalysisJson = mergedAnalysis;
        job.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        JsonElement? requirements = null;
        if (!string.IsNullOrWhiteSpace(job.RequirementsJson))
        {
            using var reqDoc = JsonDocument.Parse(job.RequirementsJson);
            requirements = reqDoc.RootElement.Clone();
        }

        using var analysisDoc = JsonDocument.Parse(mergedAnalysis);
        return new JobOfferAnalysisResponse(
            job.Id,
            job.Title,
            job.Company,
            job.Url,
            job.Description,
            job.FitScore,
            job.AnalysisStatus,
            job.UserProfileId,
            requirements,
            analysisDoc.RootElement.Clone(),
            job.CreatedAt);
    }

    private static string MergeRecruiterReplyIntoAnalysis(string? existingJson, JsonElement replyElement)
    {
        var dict = new Dictionary<string, object?>();
        if (!string.IsNullOrWhiteSpace(existingJson))
        {
            try
            {
                dict = JsonSerializer.Deserialize<Dictionary<string, object?>>(existingJson) ?? new();
            }
            catch
            {
                dict = new();
            }
        }

        dict["resposta_recrutador"] = replyElement.Clone();
        return JsonSerializer.Serialize(dict);
    }

    private async Task SaveExecutionLogAsync(
        int? jobOfferId,
        ChatResponse? response,
        long latencyMs,
        bool success,
        string? errorMessage,
        CancellationToken cancellationToken)
    {
        var provider = response?.AdditionalProperties?.TryGetValue(ResilientChatClient.ProviderKey, out var p) == true
            ? p?.ToString() ?? "Unknown"
            : "Unknown";
        var model = response?.AdditionalProperties?.TryGetValue(ResilientChatClient.ModelKey, out var m) == true
            ? m?.ToString() ?? "unknown"
            : "unknown";
        var usedFallback = response?.AdditionalProperties?.TryGetValue(ResilientChatClient.UsedFallbackKey, out var fb) == true
            && fb is true;

        var inputTokens = response?.Usage?.InputTokenCount ?? 0;
        var outputTokens = response?.Usage?.OutputTokenCount ?? 0;
        var totalTokens = response?.Usage?.TotalTokenCount ?? (inputTokens + outputTokens);
        var costUsd = CareerPilotTelemetry.EstimateCostUsd(provider, model, inputTokens, outputTokens);

        var log = new AiExecutionLog
        {
            Operation = "JobFitAnalysis",
            JobOfferId = jobOfferId,
            Provider = provider,
            Model = model,
            UsedFallback = usedFallback,
            LatencyMs = latencyMs,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            TotalTokens = totalTokens,
            EstimatedCostUsd = costUsd,
            Success = success,
            ErrorMessage = errorMessage,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.AiExecutionLogs.Add(log);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private static async Task<string> RunDirectResearchAsync(
        IList<AITool> tools,
        AnalyzeJobOfferRequest request,
        CancellationToken cancellationToken)
    {
        var sb = new System.Text.StringBuilder();
        var functions = tools.OfType<AIFunction>().ToList();

        var fetchTool = functions.FirstOrDefault(f =>
            f.Name.Contains("fetch", StringComparison.OrdinalIgnoreCase));
        var searchTool = functions.FirstOrDefault(f =>
            f.Name.Contains("search", StringComparison.OrdinalIgnoreCase));

        if (fetchTool is not null && !string.IsNullOrWhiteSpace(request.Url))
        {
            try
            {
                var fetchResult = await fetchTool.InvokeAsync(
                    new AIFunctionArguments(new Dictionary<string, object?> { ["url"] = request.Url }),
                    cancellationToken);
                sb.AppendLine($"### Conteúdo da URL da vaga ({request.Url}):");
                sb.AppendLine(fetchResult?.ToString());
            }
            catch
            {
                // Ignora erro pontual de fetch
            }
        }

        if (searchTool is not null)
        {
            var companyTerm = string.IsNullOrWhiteSpace(request.Company) ? request.Title : request.Company;
            if (!string.IsNullOrWhiteSpace(companyTerm))
            {
                try
                {
                    var companySearch = await searchTool.InvokeAsync(
                        new AIFunctionArguments(new Dictionary<string, object?>
                        {
                            ["query"] = $"{companyTerm} mission values products company"
                        }),
                        cancellationToken);
                    sb.AppendLine($"### Pesquisa sobre a empresa ({companyTerm}):");
                    sb.AppendLine(companySearch?.ToString());
                }
                catch
                {
                    // Ignora erro pontual de search
                }
            }

            if (!string.IsNullOrWhiteSpace(request.Title))
            {
                try
                {
                    var marketSearch = await searchTool.InvokeAsync(
                        new AIFunctionArguments(new Dictionary<string, object?>
                        {
                            ["query"] = $"{request.Title} job requirements skills"
                        }),
                        cancellationToken);
                    sb.AppendLine($"### Pesquisa de vagas similares ({request.Title}):");
                    sb.AppendLine(marketSearch?.ToString());
                }
                catch
                {
                    // Ignora erro pontual de search
                }
            }
        }

        return sb.Length == 0 ? "Nenhum dado adicional retornado pelas ferramentas." : sb.ToString();
    }

    private sealed class JobAnalysisAiPayload
    {
        public string? Title { get; set; }
        public string? Company { get; set; }
        public JsonElement? Requirements { get; set; }

        [JsonPropertyName("match_score")]
        public decimal? MatchScore { get; set; }
    }
}
