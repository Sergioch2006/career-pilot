namespace CareerPilot.Api.Models;

/// <summary>
/// Armazena perguntas e respostas da entrevista adaptativa sobre o candidato.
/// </summary>
public class ProfileQA
{
    public int Id { get; set; }
    public int UserProfileId { get; set; }

    public string Question { get; set; } = string.Empty;
    public string? Answer { get; set; }

    /// <summary>
    /// Categoria opcional da pergunta (ex: metrics, international, compensation, architecture).
    /// </summary>
    public string? Category { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? AnsweredAt { get; set; }

    public UserProfile UserProfile { get; set; } = null!;
}
