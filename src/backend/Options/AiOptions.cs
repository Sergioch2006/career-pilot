namespace CareerPilot.Api.Options;

public enum AiTaskType
{
    ProfileIngestion,
    JobFitAndCascade,
    ProfileInterviewer,
    EvalGates,
    MarketResearch,
    AtsResumeGenerator,
    InterviewGuide,
    Fallback
}

public class AiOptions
{
    public const string SectionName = "AI";

    // Campos legados mantidos para compatibilidade retroativa
    public string Provider { get; set; } = "Ollama";
    public string? ApiKey { get; set; }
    public string Model { get; set; } = "llama3.2";
    public string? Endpoint { get; set; }
    public AiFallbackOptions? Fallback { get; set; }

    public Dictionary<string, AiProviderConfig> Providers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public AiTaskRoutingOptions TaskRouting { get; set; } = new();
}

public class AiProviderConfig
{
    public string Type { get; set; } = string.Empty; // Google, OpenAI, OpenAICompatible, Ollama
    public string? ApiKey { get; set; }
    public string Model { get; set; } = string.Empty;
    public string? Endpoint { get; set; }
    public decimal InputCostPerMillion { get; set; }
    public decimal OutputCostPerMillion { get; set; }
}

public class AiTaskRoutingOptions
{
    public string ProfileIngestion { get; set; } = "DeepSeek";
    public string JobFitAndCascade { get; set; } = "DeepSeek";
    public string ProfileInterviewer { get; set; } = "DeepSeek";
    public string EvalGates { get; set; } = "Ollama";
    public string MarketResearch { get; set; } = "Qwen";
    public string AtsResumeGenerator { get; set; } = "Qwen";
    public string InterviewGuide { get; set; } = "Google";
    public string Fallback { get; set; } = "Ollama";
    public List<string> FallbackChain { get; set; } = ["Ollama", "Qwen", "Google"];
}

public class AiFallbackOptions
{
    public string Provider { get; set; } = string.Empty;
    public string? ApiKey { get; set; }
    public string Model { get; set; } = string.Empty;
    public string? Endpoint { get; set; }
}
