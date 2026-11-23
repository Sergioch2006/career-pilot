using System.Threading.Channels;
using CareerPilot.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace CareerPilot.Api.Services;

public interface IJobReanalysisQueue
{
    ValueTask EnqueueProfileReanalysisAsync(int profileId, CancellationToken cancellationToken = default);
}

public class JobReanalysisQueue : IJobReanalysisQueue
{
    private readonly Channel<int> _channel = Channel.CreateUnbounded<int>();

    public ValueTask EnqueueProfileReanalysisAsync(int profileId, CancellationToken cancellationToken = default) =>
        _channel.Writer.WriteAsync(profileId, cancellationToken);

    public IAsyncEnumerable<int> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}

public class JobReanalysisWorker : BackgroundService
{
    private readonly JobReanalysisQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<JobReanalysisWorker> _logger;

    public JobReanalysisWorker(
        JobReanalysisQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<JobReanalysisWorker> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var profileId in _queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await ProcessProfileReanalysisAsync(profileId, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Erro ao processar reanálise em cascata para o perfil {ProfileId}.", profileId);
            }
        }
    }

    private async Task ProcessProfileReanalysisAsync(int profileId, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CareerPilotDbContext>();
        var analyzer = scope.ServiceProvider.GetRequiredService<IJobFitAnalyzerService>();

        var jobIds = await dbContext.JobOffers
            .AsNoTracking()
            .Where(j => (j.UserProfileId == profileId || j.UserProfileId == null) && j.AnalysisStatus == "Reanalyzing")
            .Select(j => j.Id)
            .ToListAsync(cancellationToken);

        _logger.LogInformation("Iniciando reanálise em cascata de {Count} vaga(s) para o perfil {ProfileId}.", jobIds.Count, profileId);

        foreach (var jobId in jobIds)
        {
            try
            {
                await analyzer.ReanalyzeExistingAsync(jobId, profileId, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Falha ao reanalisar a vaga {JobId} após atualização do perfil {ProfileId}.", jobId, profileId);
                var job = await dbContext.JobOffers.FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken);
                if (job is not null)
                {
                    job.AnalysisStatus = "Error";
                    job.UpdatedAt = DateTime.UtcNow;
                    await dbContext.SaveChangesAsync(cancellationToken);
                }
            }
        }
    }
}
