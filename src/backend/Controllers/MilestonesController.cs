using CareerPilot.Api.Data;
using CareerPilot.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CareerPilot.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MilestonesController : ControllerBase
{
    private readonly CareerPilotDbContext _dbContext;

    public MilestonesController(CareerPilotDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var items = await _dbContext.ProjectMilestones
            .AsNoTracking()
            .OrderByDescending(m => m.IsDefault)
            .ThenBy(m => m.Id)
            .ToListAsync(cancellationToken);

        return Ok(items);
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateMilestoneRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.Description))
        {
            return BadRequest(new { error = "Título e descrição do aprendizado são obrigatórios." });
        }

        var milestone = new ProjectMilestone
        {
            Title = request.Title.Trim(),
            Description = request.Description.Trim(),
            Category = string.IsNullOrWhiteSpace(request.Category) ? "Aprendizado" : request.Category.Trim(),
            Tags = string.IsNullOrWhiteSpace(request.Tags) ? null : request.Tags.Trim(),
            IsDefault = false,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.ProjectMilestones.Add(milestone);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetAll), new { id = milestone.Id }, milestone);
    }
}

public record CreateMilestoneRequest(
    string Title,
    string Description,
    string? Category = null,
    string? Tags = null
);
