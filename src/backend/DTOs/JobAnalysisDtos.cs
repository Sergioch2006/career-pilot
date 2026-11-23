using System.Text.Json;

namespace CareerPilot.Api.DTOs;

public record AnalyzeJobOfferRequest(
    string? Title,
    string? Company,
    string? Url,
    string? Description,
    int? ProfileId = null
);

public record GenerateAtsResumeRequest(
    IReadOnlyList<int>? MilestoneIds = null,
    string Format = "preview"
);

public record JobOfferAnalysisResponse(
    int Id,
    string Title,
    string Company,
    string? Url,
    string Description,
    decimal? FitScore,
    string AnalysisStatus,
    int? UserProfileId,
    JsonElement? Requirements,
    JsonElement? FitAnalysis,
    DateTime CreatedAt
);
