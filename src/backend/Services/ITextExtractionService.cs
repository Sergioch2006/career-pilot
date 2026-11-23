namespace CareerPilot.Api.Services;

/// <summary>
/// Serviço responsável por extrair texto plano de arquivos de currículo (.pdf, .docx).
/// </summary>
public interface ITextExtractionService
{
    bool IsSupportedExtension(string extension);
    Task<string> ExtractTextAsync(Stream fileStream, string extension, CancellationToken cancellationToken = default);
}
