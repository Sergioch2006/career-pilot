namespace CareerPilot.Api.DTOs;

public record ProfileImportJsonRequest(
    string Text,
    string? FullName = null,
    string? Email = null
);
