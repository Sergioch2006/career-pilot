using CareerPilot.Api.Options;
using CareerPilot.Api.Services;
using Microsoft.Extensions.AI;

namespace CareerPilot.Api.Extensions;

public static class AiServiceCollectionExtensions
{
    public static IServiceCollection AddAgnosticAi(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AiOptions>(configuration.GetSection(AiOptions.SectionName));
        services.Configure<McpOptions>(configuration.GetSection(McpOptions.SectionName));

        services.AddSingleton<IAiProviderFactory, AiProviderFactory>();
        services.AddSingleton<IChatClient>(sp =>
            sp.GetRequiredService<IAiProviderFactory>().GetClientForTask(AiTaskType.JobFitAndCascade));

        return services;
    }
}
