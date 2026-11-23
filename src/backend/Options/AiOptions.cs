namespace CareerPilot.Api.Options;

public class AiOptions
{
    public const string SectionName = "AI";

    public string Provider { get; set; } = "Ollama";
    public string? ApiKey { get; set; }
    public string Model { get; set; } = "llama3.2";
    public string? Endpoint { get; set; }
    public AiFallbackOptions? Fallback { get; set; }
}

public class AiFallbackOptions
{
    public string Provider { get; set; } = string.Empty;
    public string? ApiKey { get; set; }
    public string Model { get; set; } = string.Empty;
    public string? Endpoint { get; set; }
}
