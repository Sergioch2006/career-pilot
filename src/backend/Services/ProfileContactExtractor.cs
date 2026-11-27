using System.Text.Json;
using System.Text.RegularExpressions;
using CareerPilot.Api.Helpers;
using Microsoft.Extensions.AI;

namespace CareerPilot.Api.Services;

public interface IProfileContactExtractor
{
    Task<(string? FullName, string? Email)> ExtractMissingAsync(
        string rawText,
        string? providedFullName,
        string? providedEmail,
        CancellationToken cancellationToken = default);
}

public class ProfileContactExtractor : IProfileContactExtractor
{
    private static readonly Regex EmailRegex = new(
        @"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}",
        RegexOptions.Compiled);

    private static readonly string[] IgnoredHeaderKeywords =
    [
        "curriculum", "currículo", "resume", "cv", "resumo", "summary",
        "profile", "perfil", "contato", "contact", "linkedin", "github",
        "telefone", "phone", "email", "e-mail", "endereço", "address"
    ];

    private readonly IChatClient? _chatClient;
    private readonly ILogger<ProfileContactExtractor> _logger;

    public ProfileContactExtractor(
        ILogger<ProfileContactExtractor> logger,
        IChatClient? chatClient = null,
        IAiProviderFactory? providerFactory = null)
    {
        _logger = logger;
        _chatClient = providerFactory?.GetClientForTask(Options.AiTaskType.ProfileIngestion) ?? chatClient;
    }

    public async Task<(string? FullName, string? Email)> ExtractMissingAsync(
        string rawText,
        string? providedFullName,
        string? providedEmail,
        CancellationToken cancellationToken = default)
    {
        var fullName = string.IsNullOrWhiteSpace(providedFullName) ? null : providedFullName.Trim();
        var email = string.IsNullOrWhiteSpace(providedEmail) ? null : providedEmail.Trim();

        if (email is null)
        {
            var match = EmailRegex.Match(rawText);
            if (match.Success)
            {
                email = match.Value;
            }
        }

        if (fullName is null)
        {
            fullName = TryExtractNameHeuristically(rawText);
        }

        // Se ainda faltar nome ou e-mail e houver IChatClient disponível, tenta extrair via IA com timeout curto.
        if ((fullName is null || email is null) && _chatClient is not null)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(10));

                var snippet = rawText.Length > 1500 ? rawText[..1500] : rawText;
                var response = await _chatClient.GetResponseAsync(
                    [
                        new ChatMessage(ChatRole.System,
                            "Extraia apenas o nome completo e o e-mail do candidato do texto do currículo. Responda somente JSON: {\"fullName\": string|null, \"email\": string|null}."),
                        new ChatMessage(ChatRole.User, snippet)
                    ],
                    cancellationToken: cts.Token);

                var cleaned = JsonCleanupHelper.StripMarkdownCodeFence(response.Text ?? string.Empty);
                using var doc = JsonDocument.Parse(cleaned);
                if (fullName is null && doc.RootElement.TryGetProperty("fullName", out var fnEl) && fnEl.ValueKind == JsonValueKind.String)
                {
                    var extractedName = fnEl.GetString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(extractedName)) fullName = extractedName;
                }
                if (email is null && doc.RootElement.TryGetProperty("email", out var emEl) && emEl.ValueKind == JsonValueKind.String)
                {
                    var extractedEmail = emEl.GetString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(extractedEmail)) email = extractedEmail;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Extração de contato via IA ignorada; mantendo heurística local.");
            }
        }

        return (fullName, email);
    }

    private static string? TryExtractNameHeuristically(string rawText)
    {
        var lines = rawText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        foreach (var rawLine in lines.Take(10))
        {
            var line = rawLine.Trim();
            if (line.Length is < 3 or > 60) continue;
            if (line.Contains('@') || line.Contains("http", StringComparison.OrdinalIgnoreCase)) continue;
            if (line.Any(char.IsDigit)) continue;

            var lower = line.ToLowerInvariant();
            if (IgnoredHeaderKeywords.Any(k => lower.Contains(k))) continue;

            var words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length is >= 2 and <= 5 && words.All(w => char.IsLetter(w[0])))
            {
                return line;
            }
        }

        return null;
    }
}
