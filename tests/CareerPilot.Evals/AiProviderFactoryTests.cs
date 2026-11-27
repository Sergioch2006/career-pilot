using System.Runtime.CompilerServices;
using CareerPilot.Api.Options;
using CareerPilot.Api.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CareerPilot.Evals;

public class AiProviderFactoryTests
{
    [Fact]
    public void GetClientForTask_EvalGates_ShouldResolveOllamaWithZeroCost()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var options = Microsoft.Extensions.Options.Options.Create(new AiOptions
        {
            Providers = new Dictionary<string, AiProviderConfig>(StringComparer.OrdinalIgnoreCase)
            {
                ["Ollama"] = new AiProviderConfig
                {
                    Type = "Ollama",
                    Model = "llama3.2",
                    Endpoint = "http://localhost:11434/v1",
                    InputCostPerMillion = 0m,
                    OutputCostPerMillion = 0m
                }
            },
            TaskRouting = new AiTaskRoutingOptions
            {
                EvalGates = "Ollama",
                Fallback = "Ollama",
                FallbackChain = ["Ollama"]
            }
        });

        var factory = new AiProviderFactory(options, services, NullLoggerFactory.Instance);

        var evalClient = factory.GetClientForTask(AiTaskType.EvalGates);
        var ollamaDirect = factory.GetClientByProvider("Ollama");

        Assert.NotNull(evalClient);
        Assert.IsType<ResilientChatClient>(evalClient);
        Assert.NotNull(ollamaDirect);
        Assert.Equal(0m, factory.EstimateCost("Ollama", 500_000, 250_000));
    }

    [Fact]
    public async Task ResilientChatClient_WhenPrimaryFailsAndOllamaOffline_ShouldFallbackToNextHealthyProvider()
    {
        var failingRemote = new OutageChatClient("DeepSeek HTTP 503");
        var offlineOllama = new OutageChatClient("Connection refused localhost:11434");
        var healthyGoogle = new StubSuccessChatClient("{\"status\":\"ok\"}");

        var client = new ResilientChatClient(
            failingRemote,
            "DeepSeek",
            "deepseek-chat",
            [
                new ChatClientTarget("Ollama", "llama3.2", offlineOllama),
                new ChatClientTarget("Google", "gemini-3.8-flash", healthyGoogle)
            ],
            (provider, inTok, outTok) => provider == "Ollama" ? 0m : 0.0005m,
            NullLogger<ResilientChatClient>.Instance);

        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "ping")]);

        Assert.Equal("{\"status\":\"ok\"}", response.Text);
        Assert.Equal("Google", response.AdditionalProperties?[ResilientChatClient.ProviderKey]?.ToString());
        Assert.Equal("gemini-3.8-flash", response.AdditionalProperties?[ResilientChatClient.ModelKey]?.ToString());
        Assert.Equal(true, response.AdditionalProperties?[ResilientChatClient.UsedFallbackKey]);
    }

    [Fact]
    public async Task ResilientChatClient_WhenRemoteFailsAndOllamaHealthy_ShouldRecordOllamaZeroCost()
    {
        var failingRemote = new OutageChatClient("DeepSeek HTTP 429");
        var healthyOllama = new StubSuccessChatClient("{\"ok\":true}", inputTokens: 1000, outputTokens: 500);

        var client = new ResilientChatClient(
            failingRemote,
            "DeepSeek",
            "deepseek-chat",
            [new ChatClientTarget("Ollama", "llama3.2", healthyOllama)],
            (provider, _, _) => provider == "Ollama" ? 0m : 0.001m,
            NullLogger<ResilientChatClient>.Instance);

        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "ping")]);

        Assert.Equal("Ollama", response.AdditionalProperties?[ResilientChatClient.ProviderKey]?.ToString());
        Assert.Equal("llama3.2", response.AdditionalProperties?[ResilientChatClient.ModelKey]?.ToString());
        Assert.Equal(true, response.AdditionalProperties?[ResilientChatClient.UsedFallbackKey]);
        Assert.Equal(0m, response.AdditionalProperties?[ResilientChatClient.EstimatedCostKey]);
    }

    private sealed class OutageChatClient : IChatClient
    {
        private readonly string _reason;

        public OutageChatClient(string reason) => _reason = reason;

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new HttpRequestException(_reason);

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    private sealed class StubSuccessChatClient : IChatClient
    {
        private readonly string _text;
        private readonly long _inTokens;
        private readonly long _outTokens;

        public StubSuccessChatClient(string text, long inputTokens = 100, long outputTokens = 50)
        {
            _text = text;
            _inTokens = inputTokens;
            _outTokens = outputTokens;
        }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, _text))
            {
                Usage = new UsageDetails
                {
                    InputTokenCount = _inTokens,
                    OutputTokenCount = _outTokens,
                    TotalTokenCount = _inTokens + _outTokens
                }
            });

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
