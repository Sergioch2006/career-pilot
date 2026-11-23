using System.ClientModel;
using CareerPilot.Api.Options;
using CareerPilot.Api.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Mscc.GenerativeAI.Microsoft;
using OllamaSharp;
using OpenAI;

namespace CareerPilot.Api.Extensions;

public static class AiServiceCollectionExtensions
{
    private const string DefaultOllamaEndpoint = "http://localhost:11434";

    public static IServiceCollection AddAgnosticAi(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AiOptions>(configuration.GetSection(AiOptions.SectionName));
        services.Configure<McpOptions>(configuration.GetSection(McpOptions.SectionName));

        services.AddSingleton<IChatClient>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<AiOptions>>().Value;
            var loggerFactory = sp.GetService<ILoggerFactory>();
            var resilienceLogger = sp.GetRequiredService<ILogger<ResilientChatClient>>();

            var (primaryProvider, primaryModel, primaryInner) = CreateInnerChatClient(
                options.Provider,
                options.Model,
                options.ApiKey,
                options.Endpoint);

            var primaryWithTools = primaryInner
                .AsBuilder()
                .UseFunctionInvocation(loggerFactory)
                .Build(sp);

            IChatClient? fallbackWithTools = null;
            string? fallbackProvider = null;
            string? fallbackModel = null;

            if (options.Fallback is not null && !string.IsNullOrWhiteSpace(options.Fallback.Provider))
            {
                try
                {
                    var (fbProvider, fbModel, fbInner) = CreateInnerChatClient(
                        options.Fallback.Provider,
                        options.Fallback.Model,
                        options.Fallback.ApiKey,
                        options.Fallback.Endpoint);

                    fallbackProvider = fbProvider;
                    fallbackModel = fbModel;
                    fallbackWithTools = fbInner
                        .AsBuilder()
                        .UseFunctionInvocation(loggerFactory)
                        .Build(sp);
                }
                catch (Exception ex)
                {
                    resilienceLogger.LogWarning(
                        ex,
                        "Provedor de fallback '{FallbackProvider}' não pôde ser inicializado.",
                        options.Fallback.Provider);
                }
            }

            return new ResilientChatClient(
                primaryWithTools,
                primaryProvider,
                primaryModel,
                fallbackWithTools,
                fallbackProvider,
                fallbackModel,
                resilienceLogger);
        });

        return services;
    }

    private static (string Provider, string Model, IChatClient Client) CreateInnerChatClient(
        string? rawProvider,
        string? rawModel,
        string? apiKey,
        string? endpoint)
    {
        var provider = rawProvider?.Trim().ToLowerInvariant() ?? "ollama";

        return provider switch
        {
            "gemini" => (
                "Gemini",
                string.IsNullOrWhiteSpace(rawModel) ? "gemini-3.8-flash" : rawModel,
                new GeminiChatClient(
                    apiKey: RequireApiKey(apiKey, "Gemini"),
                    model: string.IsNullOrWhiteSpace(rawModel) ? "gemini-3.8-flash" : rawModel)),

            "openai" => (
                "OpenAI",
                string.IsNullOrWhiteSpace(rawModel) ? "gpt-4o-mini" : rawModel,
                CreateOpenAiCompatibleClient(
                    apiKey: RequireApiKey(apiKey, "OpenAI"),
                    model: string.IsNullOrWhiteSpace(rawModel) ? "gpt-4o-mini" : rawModel,
                    endpoint: string.IsNullOrWhiteSpace(endpoint) ? null : endpoint)),

            "ollama" => (
                "Ollama",
                string.IsNullOrWhiteSpace(rawModel) ? "llama3.2" : rawModel,
                new OllamaApiClient(
                    new Uri(string.IsNullOrWhiteSpace(endpoint) ? DefaultOllamaEndpoint : endpoint),
                    string.IsNullOrWhiteSpace(rawModel) ? "llama3.2" : rawModel)),

            _ => throw new InvalidOperationException(
                $"Provedor de IA não suportado: '{rawProvider}'. Use 'Gemini', 'OpenAI' ou 'Ollama'.")
        };
    }

    private static IChatClient CreateOpenAiCompatibleClient(string apiKey, string model, string? endpoint)
    {
        var clientOptions = new OpenAIClientOptions();
        if (!string.IsNullOrWhiteSpace(endpoint))
        {
            clientOptions.Endpoint = new Uri(endpoint);
        }

        var openAiClient = new OpenAIClient(new ApiKeyCredential(apiKey), clientOptions);
        return openAiClient.GetChatClient(model).AsIChatClient();
    }

    private static string RequireApiKey(string? apiKey, string providerName)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                $"AI:ApiKey não configurada para o provedor '{providerName}'. Verifique o appsettings.Development.json.");
        }

        return apiKey;
    }
}
