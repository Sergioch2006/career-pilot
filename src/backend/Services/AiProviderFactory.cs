using System.ClientModel;
using System.Collections.Concurrent;
using CareerPilot.Api.Options;
using CareerPilot.Api.Telemetry;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Mscc.GenerativeAI.Microsoft;
using OllamaSharp;
using OpenAI;

namespace CareerPilot.Api.Services;

public interface IAiProviderFactory
{
    IChatClient GetClientForTask(AiTaskType task);
    IChatClient GetClientByProvider(string providerName);
    decimal EstimateCost(string providerName, long inputTokens, long outputTokens);
}

public class AiProviderFactory : IAiProviderFactory
{
    private const string DefaultOllamaEndpoint = "http://localhost:11434";

    private readonly AiOptions _options;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ConcurrentDictionary<AiTaskType, IChatClient> _taskClients = new();
    private readonly ConcurrentDictionary<string, ChatClientTarget> _rawTargets = new(StringComparer.OrdinalIgnoreCase);

    public AiProviderFactory(
        IOptions<AiOptions> options,
        IServiceProvider serviceProvider,
        ILoggerFactory loggerFactory)
    {
        _options = options.Value;
        _serviceProvider = serviceProvider;
        _loggerFactory = loggerFactory;
    }

    public IChatClient GetClientForTask(AiTaskType task) =>
        _taskClients.GetOrAdd(task, BuildResilientClientForTask);

    public IChatClient GetClientByProvider(string providerName)
    {
        var target = ResolveTarget(providerName)
            ?? throw new InvalidOperationException($"Provedor de IA '{providerName}' não está configurado ou não possui ApiKey.");
        return target.Client;
    }

    public decimal EstimateCost(string providerName, long inputTokens, long outputTokens)
    {
        if (providerName.Equals("Ollama", StringComparison.OrdinalIgnoreCase))
        {
            return 0m;
        }

        if (_options.Providers.TryGetValue(providerName, out var cfg))
        {
            var inputCost = (inputTokens / 1_000_000m) * cfg.InputCostPerMillion;
            var outputCost = (outputTokens / 1_000_000m) * cfg.OutputCostPerMillion;
            return Math.Round(inputCost + outputCost, 6);
        }

        return CareerPilotTelemetry.EstimateCostUsd(providerName, _options.Model, inputTokens, outputTokens);
    }

    private IChatClient BuildResilientClientForTask(AiTaskType task)
    {
        var logger = _loggerFactory.CreateLogger<ResilientChatClient>();
        var routedProviderName = GetRoutedProviderName(task);

        var candidateNames = new List<string> { routedProviderName };
        if (!string.IsNullOrWhiteSpace(_options.TaskRouting.Fallback))
        {
            candidateNames.Add(_options.TaskRouting.Fallback);
        }
        foreach (var fb in _options.TaskRouting.FallbackChain)
        {
            candidateNames.Add(fb);
        }

        var resolvedTargets = new List<ChatClientTarget>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var name in candidateNames)
        {
            if (string.IsNullOrWhiteSpace(name) || !seen.Add(name))
            {
                continue;
            }

            var target = ResolveTarget(name);
            if (target is not null)
            {
                resolvedTargets.Add(target);
            }
        }

        // Fallback legado caso AI:Providers ainda não esteja populado
        if (resolvedTargets.Count == 0)
        {
            var legacyTarget = BuildLegacyTarget();
            resolvedTargets.Add(legacyTarget);
        }

        var primary = resolvedTargets[0];
        var fallbackChain = resolvedTargets.Skip(1).ToList();

        if (!primary.Provider.Equals(routedProviderName, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation(
                "Task {Task} roteada para '{RoutedProvider}', mas sem credencial configurada. Usando '{ActiveProvider}' ({ActiveModel}) como primário.",
                task,
                routedProviderName,
                primary.Provider,
                primary.Model);
        }

        return new ResilientChatClient(
            primary.Client,
            primary.Provider,
            primary.Model,
            fallbackChain,
            EstimateCost,
            logger);
    }

    private string GetRoutedProviderName(AiTaskType task) => task switch
    {
        AiTaskType.ProfileIngestion => _options.TaskRouting.ProfileIngestion,
        AiTaskType.JobFitAndCascade => _options.TaskRouting.JobFitAndCascade,
        AiTaskType.ProfileInterviewer => _options.TaskRouting.ProfileInterviewer,
        AiTaskType.EvalGates => _options.TaskRouting.EvalGates,
        AiTaskType.MarketResearch => _options.TaskRouting.MarketResearch,
        AiTaskType.AtsResumeGenerator => _options.TaskRouting.AtsResumeGenerator,
        AiTaskType.InterviewGuide => _options.TaskRouting.InterviewGuide,
        AiTaskType.Fallback => _options.TaskRouting.Fallback,
        _ => _options.Provider
    };

    private ChatClientTarget? ResolveTarget(string providerName)
    {
        if (_rawTargets.TryGetValue(providerName, out var cached))
        {
            return cached;
        }

        if (_options.Providers.TryGetValue(providerName, out var cfg))
        {
            var type = string.IsNullOrWhiteSpace(cfg.Type) ? providerName : cfg.Type;
            if (!type.Equals("Ollama", StringComparison.OrdinalIgnoreCase) &&
                string.IsNullOrWhiteSpace(cfg.ApiKey))
            {
                return null;
            }

            var inner = CreateInnerClient(type, cfg.Model, cfg.ApiKey, cfg.Endpoint);
            var wrapped = inner.AsBuilder().UseFunctionInvocation(_loggerFactory).Build(_serviceProvider);
            var target = new ChatClientTarget(providerName, cfg.Model, wrapped);
            _rawTargets.TryAdd(providerName, target);
            return target;
        }

        // Suporte direto a "Ollama" mesmo sem entrada explícita em AI:Providers
        if (providerName.Equals("Ollama", StringComparison.OrdinalIgnoreCase))
        {
            var inner = CreateInnerClient("Ollama", "llama3.2", null, DefaultOllamaEndpoint);
            var wrapped = inner.AsBuilder().UseFunctionInvocation(_loggerFactory).Build(_serviceProvider);
            var target = new ChatClientTarget("Ollama", "llama3.2", wrapped);
            _rawTargets.TryAdd("Ollama", target);
            return target;
        }

        return null;
    }

    private ChatClientTarget BuildLegacyTarget()
    {
        var type = string.IsNullOrWhiteSpace(_options.Provider) ? "Ollama" : _options.Provider;
        var model = string.IsNullOrWhiteSpace(_options.Model) ? "llama3.2" : _options.Model;
        var inner = CreateInnerClient(type, model, _options.ApiKey, _options.Endpoint);
        var wrapped = inner.AsBuilder().UseFunctionInvocation(_loggerFactory).Build(_serviceProvider);
        return new ChatClientTarget(type, model, wrapped);
    }

    private static IChatClient CreateInnerClient(string type, string model, string? apiKey, string? endpoint)
    {
        switch (type.Trim().ToLowerInvariant())
        {
            case "google":
            case "gemini":
                return new GeminiChatClient(
                    apiKey: RequireApiKey(apiKey, type),
                    model: string.IsNullOrWhiteSpace(model) ? "gemini-3.8-flash" : model);

            case "ollama":
            {
                var rawEndpoint = string.IsNullOrWhiteSpace(endpoint) ? DefaultOllamaEndpoint : endpoint.Trim();
                // OllamaApiClient usa a URL raiz (ex: http://localhost:11434), removendo sufixo /v1 se configurado.
                if (rawEndpoint.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
                {
                    rawEndpoint = rawEndpoint[..^3];
                }
                return new OllamaApiClient(
                    new Uri(rawEndpoint),
                    string.IsNullOrWhiteSpace(model) ? "llama3.2" : model);
            }

            case "openai":
            case "openaicompatible":
            case "deepseek":
            case "qwen":
            case "kimi":
            {
                var clientOptions = new OpenAIClientOptions();
                if (!string.IsNullOrWhiteSpace(endpoint))
                {
                    clientOptions.Endpoint = new Uri(endpoint);
                }
                var openAiClient = new OpenAIClient(new ApiKeyCredential(RequireApiKey(apiKey, type)), clientOptions);
                return openAiClient.GetChatClient(model).AsIChatClient();
            }

            default:
                throw new InvalidOperationException($"Tipo de provedor de IA não suportado: '{type}'.");
        }
    }

    private static string RequireApiKey(string? apiKey, string providerName)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                $"ApiKey não configurada para o provedor '{providerName}'.");
        }
        return apiKey;
    }
}
