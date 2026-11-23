namespace CareerPilot.Api.Models;

/// <summary>
/// Registra métricas de observabilidade, latência, tokens e custo estimado das execuções de IA.
/// </summary>
public class AiExecutionLog
{
    public int Id { get; set; }
    public string Operation { get; set; } = string.Empty;
    public int? JobOfferId { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public bool UsedFallback { get; set; }
    public long LatencyMs { get; set; }
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public long TotalTokens { get; set; }
    public decimal EstimatedCostUsd { get; set; }
    public bool Success { get; set; } = true;
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
