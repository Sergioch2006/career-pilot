using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using UglyToad.PdfPig;

namespace CareerPilot.Api.Services;

public class TextExtractionService : ITextExtractionService
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf",
        ".docx"
    };

    public bool IsSupportedExtension(string extension) =>
        !string.IsNullOrWhiteSpace(extension) && SupportedExtensions.Contains(extension);

    public async Task<string> ExtractTextAsync(
        Stream fileStream,
        string extension,
        CancellationToken cancellationToken = default)
    {
        if (!IsSupportedExtension(extension))
        {
            throw new NotSupportedException($"Formato de arquivo não suportado: '{extension}'. Use .pdf ou .docx.");
        }

        // Copia para MemoryStream para garantir suporte a Seek nas bibliotecas de leitura.
        using var memoryStream = new MemoryStream();
        await fileStream.CopyToAsync(memoryStream, cancellationToken);
        memoryStream.Position = 0;

        return extension.ToLowerInvariant() switch
        {
            ".pdf" => ExtractFromPdf(memoryStream),
            ".docx" => ExtractFromDocx(memoryStream),
            _ => throw new NotSupportedException($"Formato não suportado: {extension}")
        };
    }

    private static string ExtractFromPdf(Stream stream)
    {
        var sb = new StringBuilder();
        using var document = PdfDocument.Open(stream);

        foreach (var page in document.GetPages())
        {
            if (!string.IsNullOrWhiteSpace(page.Text))
            {
                sb.AppendLine(page.Text);
            }
        }

        return sb.ToString().Trim();
    }

    private static string ExtractFromDocx(Stream stream)
    {
        var sb = new StringBuilder();
        using var wordDoc = WordprocessingDocument.Open(stream, isEditable: false);
        var body = wordDoc.MainDocumentPart?.Document?.Body;

        if (body is null)
        {
            return string.Empty;
        }

        foreach (var paragraph in body.Descendants<Paragraph>())
        {
            var text = paragraph.InnerText;
            if (!string.IsNullOrWhiteSpace(text))
            {
                sb.AppendLine(text);
            }
        }

        return sb.ToString().Trim();
    }
}
