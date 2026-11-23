using System.Diagnostics;
using CareerPilot.Api.Telemetry;
using Microsoft.Extensions.AI;
using Polly;
using Polly.CircuitBreaker;
using Polly.Fallback;
using Polly.Retry;

namespace CareerPilot.Api.Services;

/// <summary>
/// Decorador de IChatClient com Polly (Retry exponencial, Circuit Breaker e Fallback para provedor secundário)
/// e instrumentação OpenTelemetry (latência, tokens, provedor e custo estimado).
/// </summary>
public sealed class ResilientChatClient : DelegatingChatClient
{
    public const string ProviderKey = "CareerPilot.Provider";
    public const string ModelKey = "CareerPilot.Model";
    public const string UsedFallbackKey = "CareerPilot.UsedFallback";

    private readonly IChatClient? _fallbackClient;
    private readonly string _primaryProvider;
    private readonly string _primaryModel;
    private readonly string? _fallbackProvider;
    private readonly string? _fallbackModel;
    private readonly ILogger<ResilientChatClient> _logger;
    private readonly ResiliencePipeline<ChatResponse> _pipeline;

    public ResilientChatClient(
        IChatClient primaryClient,
        string primaryProvider,
        string primaryModel,
        IChatClient? fallbackClient,
        string? fallbackProvider,
        string? fallbackModel,
        ILogger<ResilientChatClient> logger)
        : base(primaryClient)
    {
        _primaryProvider = primaryProvider;
        _primaryModel = primaryModel;
        _fallbackClient = fallbackClient;
        _fallbackProvider = fallbackProvider;
        _fallbackModel = fallbackModel;
        _logger = logger;

        var builder = new ResiliencePipelineBuilder<ChatResponse>();

        if (_fallbackClient is not null)
        {
            builder.AddFallback(new FallbackStrategyOptions<ChatResponse>
            {
                ShouldHandle = new PredicateBuilder<ChatResponse>()
                    .Handle<Exception>(ex => ex is not OperationCanceledException),
                FallbackAction = async args =>
                {
                    _logger.LogWarning(
                        args.Outcome.Exception,
                        "Provedor primário {PrimaryProvider} ({PrimaryModel}) falhou. Acionando fallback para {FallbackProvider} ({FallbackModel}).",
                        _primaryProvider,
                        _primaryModel,
                        _fallbackProvider,
                        _fallbackModel);

                    CareerPilotTelemetry.AiFallbackCounter.Add(1,
                        new KeyValuePair<string, object?>("primary", _primaryProvider),
                        new KeyValuePair<string, object?>("fallback", _fallbackProvider));

                    var fallbackResponse = await _fallbackClient.GetResponseAsync(
                        args.Context.Properties.GetValue(new ResiliencePropertyKey<IEnumerable<ChatMessage>>("messages"), []),
                        args.Context.Properties.GetValue(new ResiliencePropertyKey<ChatOptions?>("options"), null),
                        args.Context.CancellationToken);

                    fallbackResponse.AdditionalProperties ??= new AdditionalPropertiesDictionary();
                    fallbackResponse.AdditionalProperties[ProviderKey] = _fallbackProvider ?? "Fallback";
                    fallbackResponse.AdditionalProperties[ModelKey] = _fallbackModel ?? "unknown";
                    fallbackResponse.AdditionalProperties[UsedFallbackKey] = true;

                    return Outcome.FromResult(fallbackResponse);
                }
            });
        }

        builder.AddCircuitBreaker(new CircuitBreakerStrategyOptions<ChatResponse>
        {
            FailureRatio = 0.5,
            SamplingDuration = TimeSpan.FromSeconds(30),
            MinimumThroughput = 3,
            BreakDuration = TimeSpan.FromSeconds(30),
            ShouldHandle = new PredicateBuilder<ChatResponse>()
                .Handle<Exception>(ex => ex is not OperationCanceledException),
            OnOpened = args =>
            {
                _logger.LogWarning("Circuit breaker aberto para {Provider} por {Duration}s.", _primaryProvider, args.BreakDuration.TotalSeconds);
                return ValueTask.CompletedTask;
            }
        });

        builder.AddRetry(new RetryStrategyOptions<ChatResponse>
        {
            MaxRetryAttempts = 2,
            Delay = TimeSpan.FromMilliseconds(500),
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true,
            ShouldHandle = new PredicateBuilder<ChatResponse>()
                .Handle<Exception>(ex => ex is not OperationCanceledException),
            OnRetry = args =>
            {
                _logger.LogWarning(
                    args.Outcome.Exception,
                    "Retry #{Attempt} para provedor {Provider} ({Model}) após {Delay}ms.",
                    args.AttemptNumber + 1,
                    _primaryProvider,
                    _primaryModel,
                    args.RetryDelay.TotalMilliseconds);
                return ValueTask.CompletedTask;
            }
        });

        _pipeline = builder.Build();
    }

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        using var activity = CareerPilotTelemetry.ActivitySource.StartActivity("ai.chat.completion");
        activity?.SetTag("ai.primary.provider", _primaryProvider);
        activity?.SetTag("ai.primary.model", _primaryModel);

        var messageList = messages as IList<ChatMessage> ?? messages.ToList();
        var context = ResilienceContextPool.Shared.Get(cancellationToken);
        context.Properties.Set(new ResiliencePropertyKey<IEnumerable<ChatMessage>>("messages"), messageList);
        context.Properties.Set(new ResiliencePropertyKey<ChatOptions?>("options"), options);

        try
        {
            var response = await _pipeline.ExecuteAsync(
                async ctx =>
                {
                    var res = await base.GetResponseAsync(messageList, options, ctx.CancellationToken);
                    res.AdditionalProperties ??= new AdditionalPropertiesDictionary();
                    res.AdditionalProperties[ProviderKey] = _primaryProvider;
                    res.AdditionalProperties[ModelKey] = _primaryModel;
                    res.AdditionalProperties[UsedFallbackKey] = false;
                    return res;
                },
                context);

            sw.Stop();
            RecordTelemetry(activity, response, sw.Elapsed.TotalMilliseconds);
            return response;
        }
        catch (Exception ex)
        {
            sw.Stop();
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            CareerPilotTelemetry.AiRequestsCounter.Add(1,
                new KeyValuePair<string, object?>("provider", _primaryProvider),
                new KeyValuePair<string, object?>("success", false));
            throw;
        }
        finally
        {
            ResilienceContextPool.Shared.Return(context);
        }
    }

    private static void RecordTelemetry(Activity? activity, ChatResponse response, double latencyMs)
    {
        var provider = response.AdditionalProperties?.TryGetValue(ProviderKey, out var p) == true
            ? p?.ToString() ?? "unknown"
            : "unknown";
        var model = response.AdditionalProperties?.TryGetValue(ModelKey, out var m) == true
            ? m?.ToString() ?? "unknown"
            : "unknown";
        var usedFallback = response.AdditionalProperties?.TryGetValue(UsedFallbackKey, out var fb) == true
            && fb is true;

        var inputTokens = response.Usage?.InputTokenCount ?? 0;
        var outputTokens = response.Usage?.OutputTokenCount ?? 0;
        var costUsd = CareerPilotTelemetry.EstimateCostUsd(provider, model, inputTokens, outputTokens);

        activity?.SetTag("ai.provider", provider);
        activity?.SetTag("ai.model", model);
        activity?.SetTag("ai.used_fallback", usedFallback);
        activity?.SetTag("ai.tokens.input", inputTokens);
        activity?.SetTag("ai.tokens.output", outputTokens);
        activity?.SetTag("ai.cost.usd", (double)costUsd);
        activity?.SetTag("ai.latency.ms", latencyMs);

        var tags = new TagList
        {
            { "provider", provider },
            { "model", model },
            { "fallback", usedFallback }
        };

        CareerPilotTelemetry.AiRequestsCounter.Add(1, tags);
        CareerPilotTelemetry.AiLatencyHistogram.Record(latencyMs, tags);
        if (inputTokens > 0) CareerPilotTelemetry.AiInputTokensCounter.Add(inputTokens, tags);
        if (outputTokens > 0) CareerPilotTelemetry.AiOutputTokensCounter.Add(outputTokens, tags);
        if (costUsd > 0) CareerPilotTelemetry.AiCostCounter.Add((double)costUsd, tags);
    }
}
