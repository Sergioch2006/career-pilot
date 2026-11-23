using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CareerPilot.Api.Data;
using CareerPilot.Api.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Playwright;

namespace CareerPilot.Api.Services;

public interface IInterviewPdfService
{
    Task<(byte[] PdfBytes, string FileName)> GenerateInterviewGuidePdfAsync(
        int jobId,
        CancellationToken cancellationToken = default);
}

public class InterviewPdfService : IInterviewPdfService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly CareerPilotDbContext _dbContext;
    private readonly IChatClient _chatClient;

    public InterviewPdfService(CareerPilotDbContext dbContext, IChatClient chatClient)
    {
        _dbContext = dbContext;
        _chatClient = chatClient;
    }

    public async Task<(byte[] PdfBytes, string FileName)> GenerateInterviewGuidePdfAsync(
        int jobId,
        CancellationToken cancellationToken = default)
    {
        var job = await _dbContext.JobOffers
            .AsNoTracking()
            .FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken);

        if (job is null)
        {
            throw new KeyNotFoundException($"Vaga {jobId} não encontrada.");
        }

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

        var script = await GenerateScriptAsync(job, profile?.FullName, profile?.RawText, qaList, cancellationToken);
        var annex = ExtractAnnexFromAnalysis(job.FitAnalysisJson);
        var candidateName = string.IsNullOrWhiteSpace(profile?.FullName) ? "Candidato" : profile.FullName.Trim();

        var html = BuildExecutiveHtml(
            company: job.Company,
            roleTitle: job.Title,
            candidateName: candidateName,
            fitScore: job.FitScore,
            script: script,
            annex: annex);

        var pdfBytes = await RenderHtmlToPdfAsync(html);
        var safeCompany = SanitizeFileName(job.Company);
        var safeCandidate = SanitizeFileName(candidateName);
        var fileName = $"Guia_Entrevista_{safeCompany}_{safeCandidate}.pdf";

        return (pdfBytes, fileName);
    }

    private async Task<InterviewScriptPayload> GenerateScriptAsync(
        Models.JobOffer job,
        string? candidateName,
        string? profileRawText,
        List<string> qaList,
        CancellationToken cancellationToken)
    {
        var systemPrompt = """
            Você é um especialista em preparação de entrevistas seguindo a skill interview-pdf-generator.
            Gere um Roteiro de Entrevista (Cola) executivo, objetivo e natural para leitura rápida durante a call.
            REGRA OBRIGATÓRIA DE IDIOMA:
            - Identifique o idioma da descrição da vaga (ex: inglês ou português).
            - Se a descrição da vaga estiver em INGLÊS, todos os textos de fala ("texto" das seções 1 a 5) e todas as 3 perguntas estratégicas ("dia_a_dia_time", "escopo_tecnico", "contrato_proximos_passos") DEVEM ser escritos em INGLÊS.
            - Se a descrição da vaga estiver em PORTUGUÊS, todos os textos de fala e todas as 3 perguntas estratégicas DEVEM ser escritos em PORTUGUÊS.
            - Mantenha o campo "foco" em português como lembrete rápido para o candidato.
            Use **negrito** (duplo asterisco) para destacar termos-chave que servirão de âncoras visuais.
            Responda EXCLUSIVAMENTE com JSON válido neste formato:
            {
              "apresentacao": { "foco": "...", "texto": "..." },
              "experiencia_tecnica": { "foco": "...", "texto": "..." },
              "times_ageis_globais": { "foco": "...", "texto": "..." },
              "motivacao_empresa": { "foco": "...", "texto": "..." },
              "expectativa_salarial": { "foco": "...", "texto": "..." },
              "perguntas_estrategicas": {
                "foco": "Escolha 2 ou 3 destas perguntas para encerrar a chamada passando segurança:",
                "dia_a_dia_time": "...",
                "escopo_tecnico": "...",
                "contrato_proximos_passos": "..."
              }
            }
            """;

        var userPrompt = $"""
            ## Vaga
            Empresa: {job.Company}
            Cargo: {job.Title}
            Descrição: {job.Description}

            ## Análise de Aderência e Dossiê da Empresa
            {job.FitAnalysisJson ?? "(não disponível)"}

            ## Candidato
            Nome: {candidateName ?? "Candidato"}
            Currículo:
            {profileRawText ?? "(não informado)"}

            ## Respostas Complementares (ProfileQA)
            {(qaList.Count == 0 ? "(nenhuma)" : string.Join("\n", qaList))}
            """;

        var response = await _chatClient.GetResponseAsync(
            [
                new ChatMessage(ChatRole.System, systemPrompt),
                new ChatMessage(ChatRole.User, userPrompt)
            ],
            cancellationToken: cancellationToken);

        var cleaned = JsonCleanupHelper.StripMarkdownCodeFence(response.Text ?? string.Empty);
        return JsonSerializer.Deserialize<InterviewScriptPayload>(cleaned, JsonOptions)
            ?? throw new InvalidOperationException("Não foi possível gerar o roteiro de entrevista em JSON válido.");
    }

    private static AnnexPayload ExtractAnnexFromAnalysis(string? fitAnalysisJson)
    {
        var annex = new AnnexPayload();
        if (string.IsNullOrWhiteSpace(fitAnalysisJson))
        {
            return annex;
        }

        try
        {
            using var doc = JsonDocument.Parse(fitAnalysisJson);
            var root = doc.RootElement;

            if (root.TryGetProperty("gaps_e_estudos", out var gapsEl) && gapsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in gapsEl.EnumerateArray())
                {
                    annex.Gaps.Add(new StudyGapItem
                    {
                        Topico = item.TryGetProperty("topico", out var t) ? t.GetString() ?? "" : "",
                        Prioridade = item.TryGetProperty("prioridade", out var p) ? p.GetString() ?? "media" : "media",
                        PlanoEstudo = item.TryGetProperty("plano_estudo", out var pe) ? pe.GetString() ?? "" : ""
                    });
                }
            }

            if (root.TryGetProperty("vagas_similares_padroes", out var marketEl) && marketEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in marketEl.EnumerateArray())
                {
                    var val = item.GetString();
                    if (!string.IsNullOrWhiteSpace(val))
                    {
                        annex.MarketPatterns.Add(val);
                    }
                }
            }
        }
        catch
        {
            // Se o JSON salvo estiver em formato inesperado, segue sem anexo
        }

        return annex;
    }

    private static string BuildExecutiveHtml(
        string company,
        string roleTitle,
        string candidateName,
        decimal? fitScore,
        InterviewScriptPayload script,
        AnnexPayload annex)
    {
        var scorePart = fitScore.HasValue ? $" | Compatibilidade: {fitScore.Value:0}%" : string.Empty;

        var sb = new StringBuilder();
        sb.Append($$"""
            <!DOCTYPE html>
            <html lang="pt-BR">
            <head>
              <meta charset="UTF-8" />
              <style>
                @page {
                  size: A4;
                  margin: 15mm 14mm;
                }
                * { box-sizing: border-box; }
                body {
                  margin: 0;
                  font-family: "Segoe UI", Arial, Helvetica, sans-serif;
                  color: #1f2933;
                  background: #ffffff;
                  font-size: 11.5px;
                  line-height: 1.55;
                }
                .header {
                  background: #263238;
                  color: #ffffff;
                  border-radius: 8px;
                  padding: 18px 22px;
                  margin-bottom: 16px;
                }
                .header h1 {
                  margin: 0 0 6px 0;
                  font-size: 19px;
                  font-weight: 700;
                  letter-spacing: -0.2px;
                }
                .header p {
                  margin: 0;
                  font-size: 11px;
                  color: #cfd8dc;
                }
                .card {
                  border: 1px solid #d7dde3;
                  border-left: 4px solid #263238;
                  border-radius: 8px;
                  padding: 14px 16px;
                  margin-bottom: 14px;
                  background: #ffffff;
                  break-inside: avoid;
                }
                .card h2 {
                  margin: 0 0 4px 0;
                  font-size: 13.5px;
                  font-weight: 700;
                  color: #102a43;
                }
                .focus {
                  margin: 0 0 10px 0;
                  font-size: 11px;
                  font-style: italic;
                  color: #52606d;
                }
                .speech-box {
                  background: #f5f7fa;
                  border-radius: 6px;
                  padding: 12px 14px;
                  color: #1f2933;
                }
                .speech-box p {
                  margin: 0 0 8px 0;
                }
                .speech-box p:last-child {
                  margin-bottom: 0;
                }
                .question-group {
                  margin-bottom: 10px;
                }
                .question-group:last-child {
                  margin-bottom: 0;
                }
                .question-label {
                  font-weight: 700;
                  font-size: 11.5px;
                  color: #102a43;
                  margin-bottom: 4px;
                }
                .tips-box {
                  background: #eef2f6;
                  border: 1px solid #d7dde3;
                  border-radius: 8px;
                  padding: 14px 18px;
                  margin-bottom: 16px;
                  break-inside: avoid;
                }
                .tips-box h3 {
                  margin: 0 0 8px 0;
                  font-size: 12.5px;
                  color: #102a43;
                }
                .tips-box ul {
                  margin: 0;
                  padding-left: 18px;
                }
                .tips-box li {
                  margin-bottom: 4px;
                }
                .annex-title {
                  font-size: 14px;
                  font-weight: 700;
                  color: #263238;
                  border-bottom: 2px solid #263238;
                  padding-bottom: 4px;
                  margin: 18px 0 12px 0;
                }
                .gap-item {
                  border: 1px solid #e2e8f0;
                  border-radius: 6px;
                  padding: 10px 12px;
                  margin-bottom: 8px;
                  background: #f8fafc;
                  break-inside: avoid;
                }
                .badge {
                  display: inline-block;
                  padding: 1px 7px;
                  border-radius: 999px;
                  font-size: 9.5px;
                  font-weight: 700;
                  text-transform: uppercase;
                  margin-left: 6px;
                }
                .badge-alta { background: #fee2e2; color: #991b1b; }
                .badge-media { background: #fef3c7; color: #92400e; }
                .badge-baixa { background: #dcfce7; color: #166534; }
              </style>
            </head>
            <body>
              <div class="header">
                <h1>Guia Rápido de Entrevista (Cola) — {{Escape(company)}}</h1>
                <p>Cargo: {{Escape(roleTitle)}} | Candidato: {{Escape(candidateName)}}{{Escape(scorePart)}}</p>
              </div>
            """);

        AppendScriptCard(sb, "1. Apresentação Pessoal (\"Tell me about yourself\")", script.Apresentacao);
        AppendScriptCard(sb, "2. Experiência Técnica (\"Tell me about your technical background\")", script.ExperienciaTecnica);
        AppendScriptCard(sb, "3. Trabalho em Times Ágeis e Globais (\"How do you handle remote and international teams?\")", script.TimesAgeisGlobais);
        AppendScriptCard(sb, $"4. Motivação (\"Why {company}?\")", script.MotivacaoEmpresa);
        AppendScriptCard(sb, "5. Expectativa Salarial (\"What are your compensation expectations?\")", script.ExpectativaSalarial);

        // Seção 6: Perguntas Estratégicas
        sb.Append("""
              <div class="card">
                <h2>6. Perguntas Estratégicas para Fazer no Final ("Do you have any questions for me?")</h2>
            """);
        sb.Append($"    <p class=\"focus\">{Escape(script.PerguntasEstrategicas?.Foco ?? "Escolha 2 ou 3 destas perguntas para encerrar a chamada passando segurança:")}</p>");
        AppendQuestionBlock(sb, "• Sobre o dia a dia do time:", script.PerguntasEstrategicas?.DiaADiaTime);
        AppendQuestionBlock(sb, "• Sobre o escopo técnico:", script.PerguntasEstrategicas?.EscopoTecnico);
        AppendQuestionBlock(sb, "• Sobre o contrato e próximos passos:", script.PerguntasEstrategicas?.ContratoProximosPassos);
        sb.Append("  </div>");

        // Bloco final de dicas rápidas
        sb.Append("""
              <div class="tips-box">
                <h3>Dicas rápidas de leitura durante a call:</h3>
                <ul>
                  <li><strong>Não leia palavra por palavra:</strong> Use os trechos em <strong>negrito</strong> como âncoras visuais para manter o contato visual com a câmera.</li>
                  <li><strong>Ritmo:</strong> Fale um pouco mais devagar do que o normal para melhorar a dicção e transmitir calma e senioridade.</li>
                </ul>
              </div>
            """);

        // Anexo: Plano de Revisão Técnica (Gaps) & Resumo de Mercado
        if (annex.Gaps.Count > 0 || annex.MarketPatterns.Count > 0)
        {
            sb.Append("""<div class="annex-title">Anexo — Plano de Estudos (Pontos Fracos) &amp; Resumo de Mercado</div>""");

            if (annex.Gaps.Count > 0)
            {
                sb.Append("""<div class="card"><h2>Plano de Revisão Técnica (Gaps Identificados)</h2>""");
                foreach (var gap in annex.Gaps)
                {
                    var priority = gap.Prioridade?.ToLowerInvariant() ?? "media";
                    var badgeClass = priority switch
                    {
                        "alta" => "badge-alta",
                        "baixa" => "badge-baixa",
                        _ => "badge-media"
                    };
                    sb.Append($"""
                        <div class="gap-item">
                          <div><strong>{Escape(gap.Topico)}</strong> <span class="badge {badgeClass}">{Escape(priority)}</span></div>
                          <div style="margin-top:4px;">{FormatAnchors(gap.PlanoEstudo)}</div>
                        </div>
                        """);
                }
                sb.Append("</div>");
            }

            if (annex.MarketPatterns.Count > 0)
            {
                sb.Append("""<div class="card"><h2>Resumo de Mercado (Padrões em Vagas Similares)</h2><div class="speech-box"><ul style="margin:0;padding-left:18px;">""");
                foreach (var pattern in annex.MarketPatterns)
                {
                    sb.Append($"<li>{FormatAnchors(pattern)}</li>");
                }
                sb.Append("</ul></div></div>");
            }
        }

        sb.Append("</body></html>");
        return sb.ToString();
    }

    private static void AppendScriptCard(StringBuilder sb, string title, ScriptSection? section)
    {
        if (section is null) return;
        sb.Append($"""
              <div class="card">
                <h2>{Escape(title)}</h2>
                <p class="focus">Foco: {Escape(section.Foco)}</p>
                <div class="speech-box">
            """);

        var paragraphs = (section.Texto ?? string.Empty)
            .Split(["\r\n\r\n", "\n\n"], StringSplitOptions.RemoveEmptyEntries);

        foreach (var paragraph in paragraphs)
        {
            sb.Append($"      <p>{FormatAnchors(paragraph.Trim())}</p>");
        }

        sb.Append("    </div>\n  </div>\n");
    }

    private static void AppendQuestionBlock(StringBuilder sb, string label, string? question)
    {
        if (string.IsNullOrWhiteSpace(question)) return;
        sb.Append($"""
                <div class="question-group">
                  <div class="question-label">{Escape(label)}</div>
                  <div class="speech-box" style="font-style:italic;">"{FormatAnchors(question.Trim().Trim('"'))}"</div>
                </div>
            """);
    }

    private static string Escape(string? value) =>
        WebUtility.HtmlEncode(value ?? string.Empty);

    private static string FormatAnchors(string? text)
    {
        var encoded = Escape(text);
        return Regex.Replace(encoded, @"\*\*(.+?)\*\*", "<strong>$1</strong>");
    }

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
            // Fallback para Microsoft Edge instalado localmente caso o Chromium do Playwright não esteja baixado.
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
                Margin = new Margin
                {
                    Top = "15mm",
                    Bottom = "15mm",
                    Left = "14mm",
                    Right = "14mm"
                }
            });
        }
    }

    private sealed class InterviewScriptPayload
    {
        public ScriptSection? Apresentacao { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("experiencia_tecnica")]
        public ScriptSection? ExperienciaTecnica { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("times_ageis_globais")]
        public ScriptSection? TimesAgeisGlobais { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("motivacao_empresa")]
        public ScriptSection? MotivacaoEmpresa { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("expectativa_salarial")]
        public ScriptSection? ExpectativaSalarial { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("perguntas_estrategicas")]
        public StrategicQuestionsSection? PerguntasEstrategicas { get; set; }
    }

    private sealed class ScriptSection
    {
        public string Foco { get; set; } = string.Empty;
        public string Texto { get; set; } = string.Empty;
    }

    private sealed class StrategicQuestionsSection
    {
        public string? Foco { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("dia_a_dia_time")]
        public string? DiaADiaTime { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("escopo_tecnico")]
        public string? EscopoTecnico { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("contrato_proximos_passos")]
        public string? ContratoProximosPassos { get; set; }
    }

    private sealed class AnnexPayload
    {
        public List<StudyGapItem> Gaps { get; } = [];
        public List<string> MarketPatterns { get; } = [];
    }

    private sealed class StudyGapItem
    {
        public string Topico { get; set; } = string.Empty;
        public string Prioridade { get; set; } = "media";
        public string PlanoEstudo { get; set; } = string.Empty;
    }
}
