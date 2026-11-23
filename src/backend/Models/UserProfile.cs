namespace CareerPilot.Api.Models;

/// <summary>
/// Armazena o perfil profissional do candidato e o texto bruto extraído de currículos.
/// </summary>
public class UserProfile
{
    public int Id { get; set; }
    public string? FullName { get; set; }
    public string? Email { get; set; }

    /// <summary>
    /// Origem da importação: "json", "pdf" ou "docx".
    /// </summary>
    public string SourceType { get; set; } = string.Empty;

    public string? SourceFileName { get; set; }

    /// <summary>
    /// Texto completo extraído do currículo ou colado pelo usuário.
    /// </summary>
    public string RawText { get; set; } = string.Empty;

    /// <summary>
    /// Representação JSON estruturada (resumo, experiências, tecnologias, idiomas, projetos, formação).
    /// </summary>
    public string? StructuredDataJson { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<ProfileQA> QuestionsAndAnswers { get; set; } = new List<ProfileQA>();
}
