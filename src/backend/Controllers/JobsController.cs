using System.Text.Json;
using CareerPilot.Api.Data;
using CareerPilot.Api.DTOs;
using CareerPilot.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CareerPilot.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class JobsController : ControllerBase
{
    private readonly CareerPilotDbContext _dbContext;
    private readonly IJobFitAnalyzerService _jobFitAnalyzerService;
    private readonly IInterviewPdfService _interviewPdfService;
    private readonly IAtsResumeService _atsResumeService;

    public JobsController(
        CareerPilotDbContext dbContext,
        IJobFitAnalyzerService jobFitAnalyzerService,
        IInterviewPdfService interviewPdfService,
        IAtsResumeService atsResumeService)
    {
        _dbContext = dbContext;
        _jobFitAnalyzerService = jobFitAnalyzerService;
        _interviewPdfService = interviewPdfService;
        _atsResumeService = atsResumeService;
    }

    [HttpPost("analyze")]
    [ProducesResponseType(typeof(JobOfferAnalysisResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Analyze(
        [FromBody] AnalyzeJobOfferRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _jobFitAnalyzerService.AnalyzeAndSaveAsync(request, cancellationToken);
            return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<JobOfferAnalysisResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var jobs = await _dbContext.JobOffers
            .AsNoTracking()
            .OrderByDescending(j => j.CreatedAt)
            .ToListAsync(cancellationToken);

        return Ok(jobs.Select(MapToResponse));
    }

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(JobOfferAnalysisResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(int id, CancellationToken cancellationToken)
    {
        var job = await _dbContext.JobOffers
            .AsNoTracking()
            .FirstOrDefaultAsync(j => j.Id == id, cancellationToken);

        if (job is null)
        {
            return NotFound(new { error = $"Vaga {id} não encontrada." });
        }

        return Ok(MapToResponse(job));
    }

    [HttpGet("{id:int}/pdf")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadInterviewPdf(int id, CancellationToken cancellationToken)
    {
        try
        {
            var (pdfBytes, fileName) = await _interviewPdfService.GenerateInterviewGuidePdfAsync(id, cancellationToken);
            return File(pdfBytes, "application/pdf", fileName);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    [HttpPost("{id:int}/resume")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GenerateAtsResume(int id, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _atsResumeService.GenerateAndSaveAsync(id, cancellationToken);
            return CreatedAtAction(
                nameof(DownloadAtsResumePdf),
                new { id },
                new
                {
                    fileName = result.FileName,
                    savedPath = result.SavedPath,
                    sizeBytes = result.PdfBytes.Length
                });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("{id:int}/resume/pdf")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadAtsResumePdf(int id, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _atsResumeService.GenerateAndSaveAsync(id, cancellationToken);
            return File(result.PdfBytes, "application/pdf", result.FileName);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("{id:int}/ats-resume")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GenerateCustomAtsResume(
        int id,
        [FromBody] GenerateAtsResumeRequest? request,
        CancellationToken cancellationToken)
    {
        try
        {
            var format = request?.Format ?? "preview";
            var output = await _atsResumeService.GenerateCustomAsync(
                id,
                request?.MilestoneIds,
                format,
                cancellationToken);

            if (output.Format == "preview")
            {
                return Ok(new
                {
                    fileName = output.FileName,
                    language = output.Language,
                    html = output.Html
                });
            }

            var contentType = output.Format == "docx"
                ? "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
                : "application/pdf";

            return File(output.FileBytes!, contentType, output.FileName);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("{id:int}/reply")]
    [ProducesResponseType(typeof(JobOfferAnalysisResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GenerateRecruiterReply(int id, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _jobFitAnalyzerService.GenerateRecruiterReplyAsync(id, cancellationToken);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    private static JobOfferAnalysisResponse MapToResponse(Models.JobOffer job)
    {
        JsonElement? requirements = null;
        if (!string.IsNullOrWhiteSpace(job.RequirementsJson))
        {
            using var doc = JsonDocument.Parse(job.RequirementsJson);
            requirements = doc.RootElement.Clone();
        }

        JsonElement? fitAnalysis = null;
        if (!string.IsNullOrWhiteSpace(job.FitAnalysisJson))
        {
            using var doc = JsonDocument.Parse(job.FitAnalysisJson);
            fitAnalysis = doc.RootElement.Clone();
        }

        return new JobOfferAnalysisResponse(
            job.Id,
            job.Title,
            job.Company,
            job.Url,
            job.Description,
            job.FitScore,
            job.AnalysisStatus,
            job.UserProfileId,
            requirements,
            fitAnalysis,
            job.CreatedAt);
    }
}
