namespace CareerPilot.Api.DTOs;

public record ProfileImportResponse(
    int Id,
    string SourceType,
    string? SourceFileName,
    string? FullName,
    string? Email,
    int CharacterCount,
    string RawText,
    DateTime CreatedAt,
    DateTime UpdatedAt
);
