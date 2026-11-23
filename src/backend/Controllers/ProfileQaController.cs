using CareerPilot.Api.Data;
using CareerPilot.Api.DTOs;
using CareerPilot.Api.Models;
using CareerPilot.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CareerPilot.Api.Controllers;

[ApiController]
[Route("api/profile/qa")]
public class ProfileQaController : ControllerBase
{
    private readonly CareerPilotDbContext _dbContext;
    private readonly IProfileInterviewerService _interviewerService;

    public ProfileQaController(
        CareerPilotDbContext dbContext,
        IProfileInterviewerService interviewerService)
    {
        _dbContext = dbContext;
        _interviewerService = interviewerService;
    }

    [HttpPost("/api/profile/interview/generate")]
    [ProducesResponseType(typeof(IEnumerable<ProfileQaResponse>), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GenerateInterviewQuestions(
        [FromQuery] int? profileId,
        CancellationToken cancellationToken)
    {
        try
        {
            var generated = await _interviewerService.GenerateQuestionsAsync(profileId, cancellationToken);
            return CreatedAtAction(nameof(GetAll), new { profileId }, generated);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<ProfileQaResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll([FromQuery] int? profileId, CancellationToken cancellationToken)
    {
        var query = _dbContext.ProfileQAs.AsNoTracking();

        if (profileId.HasValue)
        {
            query = query.Where(q => q.UserProfileId == profileId.Value);
        }

        var items = await query
            .OrderBy(q => q.CreatedAt)
            .ThenBy(q => q.Id)
            .Select(q => new ProfileQaResponse(
                q.Id,
                q.UserProfileId,
                q.Question,
                q.Answer,
                q.Category,
                q.CreatedAt,
                q.AnsweredAt))
            .ToListAsync(cancellationToken);

        return Ok(items);
    }

    [HttpPost]
    [ProducesResponseType(typeof(ProfileQaResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CreateProfileQaRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Question))
        {
            return BadRequest(new { error = "A pergunta é obrigatória." });
        }

        var targetProfileId = request.UserProfileId;
        if (!targetProfileId.HasValue)
        {
            targetProfileId = await _dbContext.UserProfiles
                .OrderByDescending(p => p.Id)
                .Select(p => (int?)p.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        if (!targetProfileId.HasValue)
        {
            return BadRequest(new { error = "Nenhum perfil encontrado. Importe seu perfil antes de adicionar perguntas." });
        }

        var profileExists = await _dbContext.UserProfiles.AnyAsync(p => p.Id == targetProfileId.Value, cancellationToken);
        if (!profileExists)
        {
            return BadRequest(new { error = $"Perfil {targetProfileId.Value} não encontrado." });
        }

        var trimmedAnswer = string.IsNullOrWhiteSpace(request.Answer) ? null : request.Answer.Trim();
        var qa = new ProfileQA
        {
            UserProfileId = targetProfileId.Value,
            Question = request.Question.Trim(),
            Answer = trimmedAnswer,
            Category = string.IsNullOrWhiteSpace(request.Category) ? null : request.Category.Trim(),
            CreatedAt = DateTime.UtcNow,
            AnsweredAt = trimmedAnswer is not null ? DateTime.UtcNow : null
        };

        _dbContext.ProfileQAs.Add(qa);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var response = new ProfileQaResponse(
            qa.Id,
            qa.UserProfileId,
            qa.Question,
            qa.Answer,
            qa.Category,
            qa.CreatedAt,
            qa.AnsweredAt);

        return CreatedAtAction(nameof(GetAll), new { profileId = qa.UserProfileId }, response);
    }

    [HttpPut("{id:int}/answer")]
    [ProducesResponseType(typeof(ProfileQaResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SaveAnswer(
        int id,
        [FromBody] SaveProfileQaAnswerRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Answer))
        {
            return BadRequest(new { error = "A resposta não pode estar vazia." });
        }

        var qa = await _dbContext.ProfileQAs.FirstOrDefaultAsync(q => q.Id == id, cancellationToken);
        if (qa is null)
        {
            return NotFound(new { error = $"Pergunta {id} não encontrada." });
        }

        qa.Answer = request.Answer.Trim();
        qa.AnsweredAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new ProfileQaResponse(
            qa.Id,
            qa.UserProfileId,
            qa.Question,
            qa.Answer,
            qa.Category,
            qa.CreatedAt,
            qa.AnsweredAt));
    }
}
