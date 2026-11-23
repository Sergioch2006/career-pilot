using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace CareerPilot.Api.Telemetry;

public static class CareerPilotTelemetry
{
    public const string ServiceName = "CareerPilot.Api";
    public const string SourceName = "CareerPilot.AI";

    public static readonly ActivitySource ActivitySource = new(SourceName);
    public static readonly Meter Meter = new(SourceName);

    public static readonly Counter<long> AiRequestsCounter =
        Meter.CreateCounter<long>("careerpilot.ai.requests", description: "Total de chamadas ao IChatClient");

    public static readonly Counter<long> AiFallbackCounter =
        Meter.CreateCounter<long>("careerpilot.ai.fallbacks", description: "Total de ativações de fallback para provedor secundário");

    public static readonly Counter<long> AiInputTokensCounter =
        Meter.CreateCounter<long>("careerpilot.ai.tokens.input", description: "Tokens de entrada consumidos");

    public static readonly Counter<long> AiOutputTokensCounter =
        Meter.CreateCounter<long>("careerpilot.ai.tokens.output", description: "Tokens de saída gerados");

    public static readonly Histogram<double> AiLatencyHistogram =
        Meter.CreateHistogram<double>("careerpilot.ai.latency.ms", unit: "ms", description: "Latência das chamadas de IA em ms");

    public static readonly Counter<double> AiCostCounter =
        Meter.CreateCounter<double>("careerpilot.ai.cost.usd", unit: "USD", description: "Custo estimado acumulado em USD");

    /// <summary>
    /// Estima o custo em USD por 1M de tokens de acordo com provedor e modelo.
    /// </summary>
    public static decimal EstimateCostUsd(string provider, string model, long inputTokens, long outputTokens)
    {
        var p = provider.Trim().ToLowerInvariant();
        var m = model.Trim().ToLowerInvariant();

        // Ollama local não possui custo por token.
        if (p == "ollama")
        {
            return 0m;
        }

        // Preços aproximados por 1M tokens (input / output)
        var (inputPerMillion, outputPerMillion) = (p, m) switch
        {
            ("gemini", var gm) when gm.Contains("pro") => (1.25m, 5.00m),
            ("gemini", _) => (0.075m, 0.30m),
            ("openai", var om) when om.Contains("gpt-4o-mini") => (0.15m, 0.60m),
            ("openai", _) => (2.50m, 10.00m),
            _ => (0.10m, 0.40m)
        };

        var inputCost = (inputTokens / 1_000_000m) * inputPerMillion;
        var outputCost = (outputTokens / 1_000_000m) * outputPerMillion;
        return Math.Round(inputCost + outputCost, 6);
    }
}
