using CareerPilot.Api.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CareerPilot.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TelemetryController : ControllerBase
{
    private readonly CareerPilotDbContext _dbContext;

    public TelemetryController(CareerPilotDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet("logs")]
    public async Task<IActionResult> GetLogs([FromQuery] int take = 20, CancellationToken cancellationToken = default)
    {
        var clampedTake = Math.Clamp(take, 1, 100);
        var logs = await _dbContext.AiExecutionLogs
            .AsNoTracking()
            .OrderByDescending(l => l.CreatedAt)
            .ThenByDescending(l => l.Id)
            .Take(clampedTake)
            .ToListAsync(cancellationToken);

        var summary = new
        {
            totalExecutions = logs.Count,
            totalInputTokens = logs.Sum(l => l.InputTokens),
            totalOutputTokens = logs.Sum(l => l.OutputTokens),
            totalEstimatedCostUsd = logs.Sum(l => l.EstimatedCostUsd),
            avgLatencyMs = logs.Count == 0 ? 0 : Math.Round(logs.Average(l => l.LatencyMs), 1)
        };

        return Ok(new { summary, logs });
    }
}
