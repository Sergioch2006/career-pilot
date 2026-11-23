using System.Text.Json;
using CareerPilot.Api.Data;
using CareerPilot.Api.DTOs;
using CareerPilot.Api.Helpers;
using CareerPilot.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;

namespace CareerPilot.Api.Services;

public interface IProfileInterviewerService
{
    Task<IReadOnlyList<ProfileQaResponse>> GenerateQuestionsAsync(int? profileId = null, CancellationToken cancellationToken = default);
}

public class ProfileInterviewerService : IProfileInterviewerService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly CareerPilotDbContext _dbContext;
    private readonly IChatClient _chatClient;
    private readonly IRequestLanguageProvider? _languageProvider;

    public ProfileInterviewerService(
        CareerPilotDbContext dbContext,
        IChatClient chatClient,
        IRequestLanguageProvider? languageProvider = null)
    {
        _dbContext = dbContext;
        _chatClient = chatClient;
        _languageProvider = languageProvider;
    }

    public async Task<IReadOnlyList<ProfileQaResponse>> GenerateQuestionsAsync(
        int? profileId = null,
        CancellationToken cancellationToken = default)
    {
        var profile = profileId.HasValue
            ? await _dbContext.UserProfiles.FirstOrDefaultAsync(p => p.Id == profileId.Value, cancellationToken)
            : await _dbContext.UserProfiles.OrderByDescending(p => p.Id).FirstOrDefaultAsync(cancellationToken);

        if (profile is null)
        {
            throw new InvalidOperationException("Nenhum perfil encontrado. Importe seu currículo antes de gerar perguntas.");
        }

        var existingQuestions = await _dbContext.ProfileQAs
            .Where(q => q.UserProfileId == profile.Id)
            .OrderBy(q => q.Id)
            .Select(q => new { q.Question, q.Answer, q.Category })
            .ToListAsync(cancellationToken);

        var existingSection = existingQuestions.Count == 0
            ? "Nenhuma pergunta anterior registrada."
            : string.Join("\n", existingQuestions.Select(q =>
                $"- [{q.Category ?? "geral"}] {q.Question} | Resposta: {q.Answer ?? "(pendente)"}"));

        var isEnglish = _languageProvider?.IsEnglish() ?? false;
        var targetLanguageLabel = isEnglish ? "ENGLISH (en-US)" : "PORTUGUESE (pt-BR)";

        var systemPrompt = $$"""
            Você é um entrevistador técnico sênior seguindo a skill profile-interviewer.
            Analise o currículo do candidato considerando: resumo, experiências, tecnologias, idiomas, projetos e formação.
            Identifique lacunas importantes para entrevistas de nível sênior (ex: métricas quantitativas de impacto, experiência com times internacionais, preferências de contrato/salário, decisões arquiteturais).
            Gere EXATAMENTE 3 perguntas objetivas em {{targetLanguageLabel}} que NÃO repitam perguntas já existentes.
            Responda também o conteúdo de "resumo_estruturado" no idioma {{targetLanguageLabel}}.
            Responda EXCLUSIVAMENTE com um JSON válido no formato:
            {
              "resumo_estruturado": {
                "resumo": "...",
                "experiencias": ["..."],
                "tecnologias": ["..."],
                "idiomas": ["..."],
                "projetos": ["..."],
                "formacao": ["..."]
              },
              "perguntas": [
                { "question": "...", "category": "metrics|international|compensation|architecture|leadership" }
              ]
            }
            """;

        var userPrompt = $"""
            ## Currículo (RawText):
            {profile.RawText}

            ## Perguntas já existentes em ProfileQA:
            {existingSection}
            """;

        var response = await _chatClient.GetResponseAsync(
            [
                new ChatMessage(ChatRole.System, systemPrompt),
                new ChatMessage(ChatRole.User, userPrompt)
            ],
            cancellationToken: cancellationToken);

        var cleanedJson = JsonCleanupHelper.StripMarkdownCodeFence(response.Text ?? string.Empty);
        var parsed = JsonSerializer.Deserialize<GeneratedInterviewPayload>(cleanedJson, JsonOptions);

        if (parsed?.Perguntas is null || parsed.Perguntas.Count == 0)
        {
            throw new InvalidOperationException("A IA não retornou perguntas válidas no formato esperado.");
        }

        if (parsed.ResumoEstruturado.HasValue)
        {
            profile.StructuredDataJson = parsed.ResumoEstruturado.Value.GetRawText();
            profile.UpdatedAt = DateTime.UtcNow;
        }

        var createdEntities = parsed.Perguntas
            .Where(q => !string.IsNullOrWhiteSpace(q.Question))
            .Take(3)
            .Select(q => new ProfileQA
            {
                UserProfileId = profile.Id,
                Question = q.Question.Trim(),
                Category = string.IsNullOrWhiteSpace(q.Category) ? "general" : q.Category.Trim(),
                CreatedAt = DateTime.UtcNow
            })
            .ToList();

        _dbContext.ProfileQAs.AddRange(createdEntities);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return createdEntities
            .Select(q => new ProfileQaResponse(
                q.Id,
                q.UserProfileId,
                q.Question,
                q.Answer,
                q.Category,
                q.CreatedAt,
                q.AnsweredAt))
            .ToList();
    }

    private sealed class GeneratedInterviewPayload
    {
        public JsonElement? ResumoEstruturado { get; set; }
        public List<GeneratedQuestionItem> Perguntas { get; set; } = [];
    }

    private sealed class GeneratedQuestionItem
    {
        public string Question { get; set; } = string.Empty;
        public string? Category { get; set; }
    }
}
