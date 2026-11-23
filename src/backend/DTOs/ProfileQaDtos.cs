namespace CareerPilot.Api.DTOs;

public record ProfileQaResponse(
    int Id,
    int UserProfileId,
    string Question,
    string? Answer,
    string? Category,
    DateTime CreatedAt,
    DateTime? AnsweredAt
);

public record CreateProfileQaRequest(
    int? UserProfileId,
    string Question,
    string? Answer = null,
    string? Category = null
);

public record SaveProfileQaAnswerRequest(
    string Answer
);
