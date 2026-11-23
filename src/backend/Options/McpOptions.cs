using System.Text.Json.Serialization;

namespace CareerPilot.Api.Options;

public class McpOptions
{
    public const string SectionName = "MCP";

    public Dictionary<string, McpServerConfig> Servers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public class McpServersFile
{
    [JsonPropertyName("mcpServers")]
    public Dictionary<string, McpServerFileEntry> McpServers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public class McpServerFileEntry
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("command")]
    public string? Command { get; set; }

    [JsonPropertyName("args")]
    public string[] Args { get; set; } = [];

    [JsonPropertyName("env")]
    public Dictionary<string, string>? Env { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }
}

public class McpServerConfig
{
    public bool Enabled { get; set; } = true;
    public string? Command { get; set; }
    public string[] Arguments { get; set; } = [];
    public Dictionary<string, string>? EnvironmentVariables { get; set; }
    public string? Url { get; set; }
}
