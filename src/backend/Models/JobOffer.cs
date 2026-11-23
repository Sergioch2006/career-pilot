namespace CareerPilot.Api.Models;

/// <summary>
/// Armazena vagas ofertadas e o resultado da análise de aderência.
/// </summary>
public class JobOffer
{
    public int Id { get; set; }
    public int? UserProfileId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Company { get; set; } = string.Empty;
    public string? Url { get; set; }
    public string Description { get; set; } = string.Empty;

    public string? RequirementsJson { get; set; }
    public decimal? FitScore { get; set; }
    public string? FitAnalysisJson { get; set; }

    /// <summary>
    /// Status da análise: "UpToDate", "Reanalyzing" ou "Error".
    /// </summary>
    public string AnalysisStatus { get; set; } = "UpToDate";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
