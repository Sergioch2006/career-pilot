using System.Text.Json;
using CareerPilot.Api.Data;
using CareerPilot.Api.DTOs;
using CareerPilot.Api.Models;
using CareerPilot.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CareerPilot.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProfileController : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly CareerPilotDbContext _dbContext;
    private readonly ITextExtractionService _textExtractionService;
    private readonly IProfileContactExtractor _contactExtractor;

    public ProfileController(
        CareerPilotDbContext dbContext,
        ITextExtractionService textExtractionService,
        IProfileContactExtractor contactExtractor)
    {
        _dbContext = dbContext;
        _textExtractionService = textExtractionService;
        _contactExtractor = contactExtractor;
    }

    /// <summary>
    /// Importa um perfil via texto colado (application/json) ou upload de arquivo .pdf / .docx (multipart/form-data).
    /// </summary>
    [HttpPost("import")]
    [ProducesResponseType(typeof(ProfileImportResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status415UnsupportedMediaType)]
    public async Task<IActionResult> Import(CancellationToken cancellationToken)
    {
        if (Request.HasFormContentType)
        {
            return await HandleMultipartImportAsync(cancellationToken);
        }

        if (Request.ContentType?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true)
        {
            return await HandleJsonImportAsync(cancellationToken);
        }

        return StatusCode(
            StatusCodes.Status415UnsupportedMediaType,
            new { error = "Content-Type deve ser 'application/json' ou 'multipart/form-data'." });
    }

    [HttpGet("latest")]
    [ProducesResponseType(typeof(ProfileImportResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetLatest(CancellationToken cancellationToken)
    {
        var profile = await _dbContext.UserProfiles
            .AsNoTracking()
            .OrderByDescending(p => p.UpdatedAt)
            .ThenByDescending(p => p.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (profile is null)
        {
            return NotFound(new { error = "Nenhum perfil importado ainda." });
        }

        return Ok(MapToResponse(profile));
    }

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(ProfileImportResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(int id, CancellationToken cancellationToken)
    {
        var profile = await _dbContext.UserProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (profile is null)
        {
            return NotFound(new { error = $"Perfil {id} não encontrado." });
        }

        return Ok(MapToResponse(profile));
    }

    private async Task<IActionResult> HandleJsonImportAsync(CancellationToken cancellationToken)
    {
        ProfileImportJsonRequest? request;
        try
        {
            request = await JsonSerializer.DeserializeAsync<ProfileImportJsonRequest>(
                Request.Body,
                JsonOptions,
                cancellationToken);
        }
        catch (JsonException)
        {
            return BadRequest(new { error = "JSON inválido." });
        }

        if (request is null || string.IsNullOrWhiteSpace(request.Text))
        {
            return BadRequest(new { error = "O campo 'text' é obrigatório e não pode estar vazio." });
        }

        var rawText = request.Text.Trim();
        var (fullName, email) = await _contactExtractor.ExtractMissingAsync(
            rawText,
            request.FullName,
            request.Email,
            cancellationToken);

        var profile = new UserProfile
        {
            FullName = fullName,
            Email = email,
            SourceType = "json",
            RawText = rawText,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _dbContext.UserProfiles.Add(profile);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = profile.Id }, MapToResponse(profile));
    }

    private async Task<IActionResult> HandleMultipartImportAsync(CancellationToken cancellationToken)
    {
        var form = await Request.ReadFormAsync(cancellationToken);
        var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();

        if (file is null || file.Length == 0)
        {
            return BadRequest(new { error = "Nenhum arquivo válido foi enviado no formulário." });
        }

        var extension = Path.GetExtension(file.FileName);
        if (!_textExtractionService.IsSupportedExtension(extension))
        {
            return BadRequest(new { error = "Formato de arquivo não suportado. Envie um arquivo .pdf ou .docx." });
        }

        await using var stream = file.OpenReadStream();
        var extractedText = await _textExtractionService.ExtractTextAsync(stream, extension, cancellationToken);

        if (string.IsNullOrWhiteSpace(extractedText))
        {
            return BadRequest(new { error = "Não foi possível extrair texto do arquivo enviado." });
        }

        var providedFullName = form.TryGetValue("fullName", out var fn) ? fn.ToString() : null;
        var providedEmail = form.TryGetValue("email", out var em) ? em.ToString() : null;

        var (fullName, email) = await _contactExtractor.ExtractMissingAsync(
            extractedText,
            providedFullName,
            providedEmail,
            cancellationToken);

        var profile = new UserProfile
        {
            FullName = fullName,
            Email = email,
            SourceType = extension.TrimStart('.').ToLowerInvariant(),
            SourceFileName = Path.GetFileName(file.FileName),
            RawText = extractedText,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _dbContext.UserProfiles.Add(profile);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = profile.Id }, MapToResponse(profile));
    }

    internal static ProfileImportResponse MapToResponse(UserProfile profile) => new(
        profile.Id,
        profile.SourceType,
        profile.SourceFileName,
        profile.FullName,
        profile.Email,
        profile.RawText.Length,
        profile.RawText,
        profile.CreatedAt,
        profile.UpdatedAt
    );
}
