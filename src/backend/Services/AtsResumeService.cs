using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using CareerPilot.Api.Data;
using CareerPilot.Api.Helpers;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Playwright;

namespace CareerPilot.Api.Services;

public interface IAtsResumeService
{
    Task<AtsResumeResult> GenerateAndSaveAsync(int jobId, CancellationToken cancellationToken = default);

    Task<AtsResumeOutput> GenerateCustomAsync(
        int jobId,
        IReadOnlyList<int>? milestoneIds,
        string format,
        CancellationToken cancellationToken = default);
}

public record AtsResumeResult(
    string FileName,
    string SavedPath,
    byte[] PdfBytes
);

public record AtsResumeOutput(
    string Format,
    string FileName,
    string Language,
    string Html,
    string? SavedPath,
    byte[]? FileBytes
);

public class AtsResumeService : IAtsResumeService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly CareerPilotDbContext _dbContext;
    private readonly IChatClient _chatClient;
    private readonly IHostEnvironment _environment;

    public AtsResumeService(
        CareerPilotDbContext dbContext,
        IChatClient chatClient,
        IHostEnvironment environment,
        IAiProviderFactory? providerFactory = null)
    {
        _dbContext = dbContext;
        _chatClient = providerFactory?.GetClientForTask(Options.AiTaskType.AtsResumeGenerator) ?? chatClient;
        _environment = environment;
    }

    public async Task<AtsResumeResult> GenerateAndSaveAsync(
        int jobId,
        CancellationToken cancellationToken = default)
    {
        var output = await GenerateCustomAsync(jobId, null, "pdf", cancellationToken);
        return new AtsResumeResult(output.FileName, output.SavedPath!, output.FileBytes!);
    }

    public async Task<AtsResumeOutput> GenerateCustomAsync(
        int jobId,
        IReadOnlyList<int>? milestoneIds,
        string format,
        CancellationToken cancellationToken = default)
    {
        var normalizedFormat = string.IsNullOrWhiteSpace(format)
            ? "pdf"
            : format.Trim().ToLowerInvariant();

        if (normalizedFormat is not ("preview" or "pdf" or "docx"))
        {
            throw new ArgumentException("Formato inválido. Use 'preview', 'pdf' ou 'docx'.");
        }

        var job = await _dbContext.JobOffers
            .AsNoTracking()
            .FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken)
            ?? throw new KeyNotFoundException($"Vaga {jobId} não encontrada.");

        var profile = await _dbContext.UserProfiles
            .AsNoTracking()
            .OrderByDescending(p => p.Id)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("Nenhum perfil encontrado. Importe seu currículo primeiro.");

        var qaList = await _dbContext.ProfileQAs
            .AsNoTracking()
            .Where(q => q.UserProfileId == profile.Id && q.Answer != null)
            .OrderBy(q => q.Id)
            .Select(q => $"- [{q.Category ?? "geral"}] {q.Question} => {q.Answer}")
            .ToListAsync(cancellationToken);

        var selectedMilestones = milestoneIds is { Count: > 0 }
            ? await _dbContext.ProjectMilestones
                .AsNoTracking()
                .Where(m => milestoneIds.Contains(m.Id))
                .OrderBy(m => m.Id)
                .ToListAsync(cancellationToken)
            : [];

        var resumeData = await GenerateTailoredResumeDataAsync(
            job,
            profile,
            qaList,
            selectedMilestones,
            cancellationToken);

        var html = BuildAtsHtml(resumeData);
        var isEnglish = string.Equals(resumeData.Language, "en", StringComparison.OrdinalIgnoreCase);
        var prefix = isEnglish ? "Resume" : "Curriculo";
        var candidateName = string.IsNullOrWhiteSpace(resumeData.FullName)
            ? (profile.FullName ?? "Candidate")
            : resumeData.FullName;

        var baseFileName = $"{prefix}_{SanitizeFileName(job.Company)}_{SanitizeFileName(candidateName)}";

        if (normalizedFormat == "preview")
        {
            return new AtsResumeOutput("preview", $"{baseFileName}.pdf", resumeData.Language, html, null, null);
        }

        var outputDir = ResolveDocsCurriculoDirectory();
        Directory.CreateDirectory(outputDir);

        if (normalizedFormat == "docx")
        {
            var docxFileName = $"{baseFileName}.docx";
            var docxBytes = BuildAtsDocx(resumeData);
            var savedDocxPath = Path.Combine(outputDir, docxFileName);
            await File.WriteAllBytesAsync(savedDocxPath, docxBytes, cancellationToken);
            return new AtsResumeOutput("docx", docxFileName, resumeData.Language, html, savedDocxPath, docxBytes);
        }

        var pdfFileName = $"{baseFileName}.pdf";
        var pdfBytes = await RenderHtmlToPdfAsync(html);
        var savedPdfPath = Path.Combine(outputDir, pdfFileName);
        await File.WriteAllBytesAsync(savedPdfPath, pdfBytes, cancellationToken);
        return new AtsResumeOutput("pdf", pdfFileName, resumeData.Language, html, savedPdfPath, pdfBytes);
    }

    private string ResolveDocsCurriculoDirectory()
    {
        var dir = new DirectoryInfo(_environment.ContentRootPath);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "docs")))
        {
            dir = dir.Parent;
        }

        var repoRoot = dir?.FullName ?? _environment.ContentRootPath;
        return Path.Combine(repoRoot, "docs", "curriculo");
    }

    private async Task<AtsResumePayload> GenerateTailoredResumeDataAsync(
        Models.JobOffer job,
        Models.UserProfile profile,
        List<string> qaList,
        List<Models.ProjectMilestone> selectedMilestones,
        CancellationToken cancellationToken)
    {
        var systemPrompt = """
            Você é um especialista em currículos otimizados para sistemas ATS (Applicant Tracking Systems) seguindo a skill ats-resume-enhancer.
            Adapte o currículo do candidato para a vaga informada usando:
            1) Fatos reais do currículo base e das respostas de ProfileQA.
            2) Os aprendizados/marcos técnicos selecionados pelo candidato (quando fornecidos), integrando-os de forma natural na seção "projects" e/ou "skills".
            Nunca invente experiências que não estejam no currículo ou nos marcos selecionados.

            REGRA OBRIGATÓRIA DE IDIOMA:
            - Identifique o idioma da descrição da vaga.
            - Se a vaga estiver em INGLÊS, defina "language": "en" e escreva TODO o currículo em INGLÊS (traduzindo inclusive os marcos selecionados para inglês técnico natural).
            - Se a vaga estiver em PORTUGUÊS, defina "language": "pt" e escreva TODO o currículo em PORTUGUÊS.

            REGRAS ATS:
            - Extraia as palavras-chave exatas (hard skills e termos arquiteturais) da vaga alvo.
            - Layout limpo de coluna única, sem gráficos, tabelas complexas ou ícones.
            - Bullet points no formato Ação + Tecnologia + Resultado.
            - Incorpore os aprendizados selecionados do CareerPilot na seção "projects" (Relevant Projects) traduzindo-os para a linguagem e palavras-chave que o ATS da vaga procura.
            - Responda EXCLUSIVAMENTE com JSON válido neste formato:
            {
              "language": "en|pt",
              "full_name": "...",
              "target_title": "...",
              "contact_line": "Email | LinkedIn | Location",
              "summary": "...",
              "skills": ["..."],
              "experiences": [
                {
                  "role": "...",
                  "company": "...",
                  "period": "...",
                  "bullets": ["..."]
                }
              ],
              "projects": [
                { "name": "...", "description": "..." }
              ],
              "education": ["..."],
              "languages": ["..."]
            }
            """;

        var milestonesSection = selectedMilestones.Count == 0
            ? "(nenhum marco adicional selecionado)"
            : string.Join("\n", selectedMilestones.Select(m =>
                $"- [{m.Category}] {m.Title}: {m.Description} (Tags: {m.Tags ?? "-"})"));

        var userPrompt = $"""
            ## Vaga Alvo
            Cargo: {job.Title}
            Empresa: {job.Company}
            Descrição: {job.Description}
            Requisitos extraídos: {job.RequirementsJson ?? "(não disponível)"}

            ## Currículo Base do Candidato
            Nome: {profile.FullName ?? "(extrair do texto)"}
            Email: {profile.Email ?? "(extrair do texto)"}
            Texto completo:
            {profile.RawText}

            ## Respostas Complementares (ProfileQA)
            {(qaList.Count == 0 ? "(nenhuma)" : string.Join("\n", qaList))}

            ## Aprendizados / Marcos Técnicos Selecionados para Injetar (ProjectMilestones)
            {milestonesSection}
            """;

        var response = await _chatClient.GetResponseAsync(
            [
                new ChatMessage(ChatRole.System, systemPrompt),
                new ChatMessage(ChatRole.User, userPrompt)
            ],
            new ChatOptions { ResponseFormat = ChatResponseFormat.Json },
            cancellationToken);

        var cleaned = JsonCleanupHelper.StripMarkdownCodeFence(response.Text ?? string.Empty);
        return JsonSerializer.Deserialize<AtsResumePayload>(cleaned, JsonOptions)
            ?? throw new InvalidOperationException("Não foi possível gerar o currículo ATS em JSON válido.");
    }

    private static string BuildAtsHtml(AtsResumePayload data)
    {
        var isEn = string.Equals(data.Language, "en", StringComparison.OrdinalIgnoreCase);

        var lblSummary = isEn ? "SUMMARY" : "RESUMO PROFISSIONAL";
        var lblSkills = isEn ? "CORE SKILLS" : "COMPETÊNCIAS E TECNOLOGIAS";
        var lblExperience = isEn ? "PROFESSIONAL EXPERIENCE" : "EXPERIÊNCIA PROFISSIONAL";
        var lblProjects = isEn ? "RELEVANT PROJECTS" : "PROJETOS RELEVANTES";
        var lblEducation = isEn ? "EDUCATION" : "FORMAÇÃO ACADÊMICA";
        var lblLanguages = isEn ? "LANGUAGES" : "IDIOMAS";

        var sb = new StringBuilder();
        sb.Append($$"""
            <!DOCTYPE html>
            <html lang="{{(isEn ? "en" : "pt-BR")}}">
            <head>
              <meta charset="UTF-8" />
              <style>
                @page { size: A4; margin: 14mm 15mm; }
                * { box-sizing: border-box; }
                body {
                  margin: 0;
                  font-family: Arial, Helvetica, sans-serif;
                  color: #111827;
                  font-size: 10.5px;
                  line-height: 1.45;
                  background: #ffffff;
                }
                .header {
                  border-bottom: 1.5px solid #111827;
                  padding-bottom: 8px;
                  margin-bottom: 12px;
                }
                .name { margin: 0; font-size: 19px; font-weight: 700; letter-spacing: 0.2px; }
                .target-title { margin: 2px 0 4px 0; font-size: 11.5px; font-weight: 600; color: #374151; }
                .contact { margin: 0; font-size: 9.8px; color: #4b5563; }
                h2 {
                  margin: 12px 0 5px 0;
                  font-size: 11px;
                  font-weight: 700;
                  letter-spacing: 0.6px;
                  border-bottom: 1px solid #d1d5db;
                  padding-bottom: 2px;
                  color: #111827;
                }
                p { margin: 0 0 6px 0; }
                .exp-item { margin-bottom: 9px; break-inside: avoid; }
                .exp-header { display: flex; justify-content: space-between; align-items: baseline; font-weight: 700; font-size: 10.8px; }
                .exp-company { font-weight: 600; color: #374151; margin-bottom: 3px; }
                ul { margin: 2px 0 0 0; padding-left: 16px; }
                li { margin-bottom: 2.5px; }
              </style>
            </head>
            <body>
              <div class="header">
                <h1 class="name">{{Esc(data.FullName)}}</h1>
                {{(string.IsNullOrWhiteSpace(data.TargetTitle) ? "" : $"<p class=\"target-title\">{Esc(data.TargetTitle)}</p>")}}
                {{(string.IsNullOrWhiteSpace(data.ContactLine) ? "" : $"<p class=\"contact\">{Esc(data.ContactLine)}</p>")}}
              </div>
            """);

        if (!string.IsNullOrWhiteSpace(data.Summary))
            sb.Append($"<h2>{lblSummary}</h2><p>{Esc(data.Summary)}</p>");

        if (data.Skills.Count > 0)
            sb.Append($"<h2>{lblSkills}</h2><p>{Esc(string.Join(" · ", data.Skills))}</p>");

        if (data.Experiences.Count > 0)
        {
            sb.Append($"<h2>{lblExperience}</h2>");
            foreach (var exp in data.Experiences)
            {
                sb.Append("<div class=\"exp-item\">");
                sb.Append($"<div class=\"exp-header\"><span>{Esc(exp.Role)}</span><span>{Esc(exp.Period)}</span></div>");
                if (!string.IsNullOrWhiteSpace(exp.Company))
                    sb.Append($"<div class=\"exp-company\">{Esc(exp.Company)}</div>");
                if (exp.Bullets.Count > 0)
                {
                    sb.Append("<ul>");
                    foreach (var b in exp.Bullets) sb.Append($"<li>{Esc(b)}</li>");
                    sb.Append("</ul>");
                }
                sb.Append("</div>");
            }
        }

        if (data.Projects.Count > 0)
        {
            sb.Append($"<h2>{lblProjects}</h2><ul>");
            foreach (var proj in data.Projects)
                sb.Append($"<li><strong>{Esc(proj.Name)}:</strong> {Esc(proj.Description)}</li>");
            sb.Append("</ul>");
        }

        if (data.Education.Count > 0)
        {
            sb.Append($"<h2>{lblEducation}</h2><ul>");
            foreach (var edu in data.Education) sb.Append($"<li>{Esc(edu)}</li>");
            sb.Append("</ul>");
        }

        if (data.Languages.Count > 0)
            sb.Append($"<h2>{lblLanguages}</h2><p>{Esc(string.Join(" · ", data.Languages))}</p>");

        sb.Append("</body></html>");
        return sb.ToString();
    }

    private static byte[] BuildAtsDocx(AtsResumePayload data)
    {
        var isEn = string.Equals(data.Language, "en", StringComparison.OrdinalIgnoreCase);

        using var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document, true))
        {
            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new Document();
            var body = mainPart.Document.AppendChild(new Body());

            body.AppendChild(CreateParagraph(data.FullName, bold: true, fontSizeHalfPts: "34"));
            if (!string.IsNullOrWhiteSpace(data.TargetTitle))
                body.AppendChild(CreateParagraph(data.TargetTitle, bold: true, fontSizeHalfPts: "22"));
            if (!string.IsNullOrWhiteSpace(data.ContactLine))
                body.AppendChild(CreateParagraph(data.ContactLine, bold: false, fontSizeHalfPts: "19"));

            if (!string.IsNullOrWhiteSpace(data.Summary))
            {
                body.AppendChild(CreateSectionHeading(isEn ? "SUMMARY" : "RESUMO PROFISSIONAL"));
                body.AppendChild(CreateParagraph(data.Summary));
            }

            if (data.Skills.Count > 0)
            {
                body.AppendChild(CreateSectionHeading(isEn ? "CORE SKILLS" : "COMPETÊNCIAS E TECNOLOGIAS"));
                body.AppendChild(CreateParagraph(string.Join(" | ", data.Skills)));
            }

            if (data.Experiences.Count > 0)
            {
                body.AppendChild(CreateSectionHeading(isEn ? "PROFESSIONAL EXPERIENCE" : "EXPERIÊNCIA PROFISSIONAL"));
                foreach (var exp in data.Experiences)
                {
                    var header = string.IsNullOrWhiteSpace(exp.Period) ? exp.Role : $"{exp.Role} ({exp.Period})";
                    body.AppendChild(CreateParagraph(header, bold: true));
                    if (!string.IsNullOrWhiteSpace(exp.Company))
                        body.AppendChild(CreateParagraph(exp.Company, bold: false));
                    foreach (var b in exp.Bullets)
                        body.AppendChild(CreateParagraph($"• {b}"));
                }
            }

            if (data.Projects.Count > 0)
            {
                body.AppendChild(CreateSectionHeading(isEn ? "RELEVANT PROJECTS" : "PROJETOS RELEVANTES"));
                foreach (var proj in data.Projects)
                    body.AppendChild(CreateParagraph($"• {proj.Name}: {proj.Description}"));
            }

            if (data.Education.Count > 0)
            {
                body.AppendChild(CreateSectionHeading(isEn ? "EDUCATION" : "FORMAÇÃO ACADÊMICA"));
                foreach (var edu in data.Education)
                    body.AppendChild(CreateParagraph($"• {edu}"));
            }

            if (data.Languages.Count > 0)
            {
                body.AppendChild(CreateSectionHeading(isEn ? "LANGUAGES" : "IDIOMAS"));
                body.AppendChild(CreateParagraph(string.Join(" | ", data.Languages)));
            }

            mainPart.Document.Save();
        }

        return ms.ToArray();
    }

    private static Paragraph CreateSectionHeading(string title) =>
        CreateParagraph(title, bold: true, fontSizeHalfPts: "22", spaceBefore: "160", spaceAfter: "60");

    private static Paragraph CreateParagraph(
        string text,
        bool bold = false,
        string fontSizeHalfPts = "20",
        string spaceBefore = "0",
        string spaceAfter = "60")
    {
        var runProps = new RunProperties(
            new RunFonts { Ascii = "Arial", HighAnsi = "Arial" },
            new FontSize { Val = fontSizeHalfPts });
        if (bold) runProps.AppendChild(new Bold());

        var run = new Run(runProps, new Text(text) { Space = SpaceProcessingModeValues.Preserve });
        var pProps = new ParagraphProperties(
            new SpacingBetweenLines { Before = spaceBefore, After = spaceAfter });

        return new Paragraph(pProps, run);
    }

    private static string Esc(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

    private static string SanitizeFileName(string input)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(input.Select(c => invalid.Contains(c) || char.IsWhiteSpace(c) ? '_' : c).ToArray());
        return Regex.Replace(cleaned, "_+", "_").Trim('_');
    }

    private static async Task<byte[]> RenderHtmlToPdfAsync(string html)
    {
        using var playwright = await Playwright.CreateAsync();

        IBrowser browser;
        try
        {
            browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        }
        catch
        {
            browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Channel = "msedge",
                Headless = true
            });
        }

        await using (browser)
        {
            var page = await browser.NewPageAsync();
            await page.SetContentAsync(html, new PageSetContentOptions { WaitUntil = WaitUntilState.NetworkIdle });
            return await page.PdfAsync(new PagePdfOptions
            {
                Format = "A4",
                PrintBackground = true,
                Margin = new Margin { Top = "14mm", Bottom = "14mm", Left = "15mm", Right = "15mm" }
            });
        }
    }

    private sealed class AtsResumePayload
    {
        public string Language { get; set; } = "pt";

        [JsonPropertyName("full_name")]
        public string FullName { get; set; } = string.Empty;

        [JsonPropertyName("target_title")]
        public string? TargetTitle { get; set; }

        [JsonPropertyName("contact_line")]
        public string? ContactLine { get; set; }

        public string? Summary { get; set; }
        public List<string> Skills { get; set; } = [];
        public List<ExperienceItem> Experiences { get; set; } = [];
        public List<ProjectItem> Projects { get; set; } = [];
        public List<string> Education { get; set; } = [];
        public List<string> Languages { get; set; } = [];
    }

    private sealed class ExperienceItem
    {
        public string Role { get; set; } = string.Empty;
        public string? Company { get; set; }
        public string? Period { get; set; }
        public List<string> Bullets { get; set; } = [];
    }

    private sealed class ProjectItem
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }
}
