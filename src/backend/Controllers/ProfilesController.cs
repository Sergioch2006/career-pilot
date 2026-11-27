using System.Text.Json;
using CareerPilot.Api.Data;
using CareerPilot.Api.DTOs;
using CareerPilot.Api.Models;
using CareerPilot.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CareerPilot.Api.Controllers;

[ApiController]
[Route("api/profiles")]
public class ProfilesController : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly CareerPilotDbContext _dbContext;
    private readonly ITextExtractionService _textExtractionService;
    private readonly IProfileContactExtractor _contactExtractor;
    private readonly IJobReanalysisQueue _reanalysisQueue;

    public ProfilesController(
        CareerPilotDbContext dbContext,
        ITextExtractionService textExtractionService,
        IProfileContactExtractor contactExtractor,
        IJobReanalysisQueue reanalysisQueue)
    {
        _dbContext = dbContext;
        _textExtractionService = textExtractionService;
        _contactExtractor = contactExtractor;
        _reanalysisQueue = reanalysisQueue;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<ProfileImportResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var profiles = await _dbContext.UserProfiles
            .AsNoTracking()
            .OrderByDescending(p => p.UpdatedAt)
            .ThenByDescending(p => p.Id)
            .ToListAsync(cancellationToken);

        return Ok(profiles.Select(MapToResponse));
    }

    [HttpGet("latest")]
    [HttpGet("/api/profile/latest")]
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
    [HttpGet("/api/profile/{id:int}")]
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

    /// <summary>
    /// Importa um perfil via texto colado (application/json) ou upload de arquivo .pdf / .docx (multipart/form-data).
    /// </summary>
    [HttpPost("import")]
    [HttpPost("/api/profile/import")]
    [ProducesResponseType(typeof(ProfileImportResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status415UnsupportedMediaType)]
    public async Task<IActionResult> Import(CancellationToken cancellationToken)
    {
        var (payload, errorResult) = await ParseProfileRequestAsync(cancellationToken);
        if (errorResult is not null || payload is null)
        {
            return errorResult!;
        }

        var now = DateTime.UtcNow;
        var profile = new UserProfile
        {
            FullName = payload.FullName,
            Email = payload.Email,
            SourceType = payload.SourceType,
            SourceFileName = payload.SourceFileName,
            RawText = payload.RawText,
            CreatedAt = now,
            UpdatedAt = now
        };

        _dbContext.UserProfiles.Add(profile);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = profile.Id }, MapToResponse(profile));
    }

    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(ProfileImportResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status415UnsupportedMediaType)]
    public async Task<IActionResult> Update(int id, CancellationToken cancellationToken)
    {
        var profile = await _dbContext.UserProfiles.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (profile is null)
        {
            return NotFound(new { error = $"Perfil {id} não encontrado." });
        }

        var (payload, errorResult) = await ParseProfileRequestAsync(cancellationToken);
        if (errorResult is not null || payload is null)
        {
            return errorResult!;
        }

        profile.FullName = payload.FullName ?? profile.FullName;
        profile.Email = payload.Email ?? profile.Email;
        profile.SourceType = payload.SourceType;
        profile.SourceFileName = payload.SourceFileName;
        profile.RawText = payload.RawText;
        profile.UpdatedAt = DateTime.UtcNow;

        // Marca todas as vagas vinculadas (ou legadas sem vínculo) como Reanalyzing antes de enfileirar.
        var linkedJobs = await _dbContext.JobOffers
            .Where(j => j.UserProfileId == profile.Id || j.UserProfileId == null)
            .ToListAsync(cancellationToken);

        foreach (var job in linkedJobs)
        {
            job.UserProfileId = profile.Id;
            job.AnalysisStatus = "Reanalyzing";
            job.UpdatedAt = DateTime.UtcNow;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        if (linkedJobs.Count > 0)
        {
            await _reanalysisQueue.EnqueueProfileReanalysisAsync(profile.Id, cancellationToken);
        }

        return Ok(MapToResponse(profile));
    }

    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var profile = await _dbContext.UserProfiles.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (profile is null)
        {
            return NotFound(new { error = $"Perfil {id} não encontrado." });
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        await _dbContext.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = ON;", cancellationToken);

        // 1. Coleta os IDs das vagas vinculadas ao perfil para limpar logs de execução relacionados.
        var linkedJobIds = await _dbContext.JobOffers
            .Where(j => j.UserProfileId == id)
            .Select(j => j.Id)
            .ToListAsync(cancellationToken);

        if (linkedJobIds.Count > 0)
        {
            await _dbContext.AiExecutionLogs
                .Where(l => l.JobOfferId != null && linkedJobIds.Contains(l.JobOfferId.Value))
                .ExecuteDeleteAsync(cancellationToken);
        }

        // 2. Exclusão explícita das tabelas relacionadas.
        await _dbContext.JobOffers
            .Where(j => j.UserProfileId == id)
            .ExecuteDeleteAsync(cancellationToken);

        await _dbContext.ProfileQAs
            .Where(q => q.UserProfileId == id)
            .ExecuteDeleteAsync(cancellationToken);

        // 3. Limpeza manual de tabelas virtuais do sqlite-vec (que não suportam FK cascade).
        await DeleteSqliteVecEmbeddingsAsync(id, cancellationToken);

        // 4. Remove o perfil principal e confirma a transação.
        _dbContext.UserProfiles.Remove(profile);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return NoContent();
    }

    private async Task<(ParsedProfilePayload? Payload, IActionResult? ErrorResult)> ParseProfileRequestAsync(
        CancellationToken cancellationToken)
    {
        string rawText;
        string sourceType;
        string? sourceFileName = null;
        string? providedFullName;
        string? providedEmail;

        if (Request.HasFormContentType)
        {
            var form = await Request.ReadFormAsync(cancellationToken);
            var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();
            if (file is null || file.Length == 0)
            {
                return (null, BadRequest(new { error = "Nenhum arquivo válido foi enviado no formulário." }));
            }

            var extension = Path.GetExtension(file.FileName);
            if (!_textExtractionService.IsSupportedExtension(extension))
            {
                return (null, BadRequest(new { error = "Formato de arquivo não suportado. Envie um arquivo .pdf ou .docx." }));
            }

            await using var stream = file.OpenReadStream();
            rawText = await _textExtractionService.ExtractTextAsync(stream, extension, cancellationToken);
            if (string.IsNullOrWhiteSpace(rawText))
            {
                return (null, BadRequest(new { error = "Não foi possível extrair texto do arquivo enviado." }));
            }

            sourceType = extension.TrimStart('.').ToLowerInvariant();
            sourceFileName = Path.GetFileName(file.FileName);
            providedFullName = form.TryGetValue("fullName", out var fn) ? fn.ToString() : null;
            providedEmail = form.TryGetValue("email", out var em) ? em.ToString() : null;
        }
        else if (Request.ContentType?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true)
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
                return (null, BadRequest(new { error = "JSON inválido." }));
            }

            if (request is null || string.IsNullOrWhiteSpace(request.Text))
            {
                return (null, BadRequest(new { error = "O campo 'text' é obrigatório e não pode estar vazio." }));
            }

            rawText = request.Text.Trim();
            sourceType = "json";
            providedFullName = request.FullName;
            providedEmail = request.Email;
        }
        else
        {
            var unsupported = StatusCode(
                StatusCodes.Status415UnsupportedMediaType,
                new { error = "Content-Type deve ser 'application/json' ou 'multipart/form-data'." });
            return (null, unsupported);
        }

        var (fullName, email) = await _contactExtractor.ExtractMissingAsync(
            rawText,
            providedFullName,
            providedEmail,
            cancellationToken);

        return (new ParsedProfilePayload(rawText, sourceType, sourceFileName, fullName, email), null);
    }

    private async Task DeleteSqliteVecEmbeddingsAsync(int profileId, CancellationToken cancellationToken)
    {
        var connection = _dbContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        var vecTables = new List<string>();
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND (name LIKE 'vec_%' OR name = 'ProfileEmbeddings');";
            using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                vecTables.Add(reader.GetString(0));
            }
        }

        foreach (var table in vecTables)
        {
            try
            {
                using var delCmd = connection.CreateCommand();
                delCmd.CommandText = $"DELETE FROM \"{table}\" WHERE user_profile_id = @pid OR profile_id = @pid;";
                var param = delCmd.CreateParameter();
                param.ParameterName = "@pid";
                param.Value = profileId;
                delCmd.Parameters.Add(param);
                await delCmd.ExecuteNonQueryAsync(cancellationToken);
            }
            catch
            {
                // Ignora caso a tabela virtual possua schema distinto
            }
        }
    }

    private static ProfileImportResponse MapToResponse(UserProfile profile) => new(
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

    private sealed record ParsedProfilePayload(
        string RawText,
        string SourceType,
        string? SourceFileName,
        string? FullName,
        string? Email);
}
