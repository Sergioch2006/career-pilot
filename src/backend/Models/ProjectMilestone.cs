namespace CareerPilot.Api.Models;

/// <summary>
/// Armazena marcos técnicos e aprendizados de projetos para injeção em currículos ATS.
/// </summary>
public class ProjectMilestone
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = "Engenharia";
    public string? Tags { get; set; }
    public bool IsDefault { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
