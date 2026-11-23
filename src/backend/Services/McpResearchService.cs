using System.ComponentModel;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using CareerPilot.Api.Options;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;

namespace CareerPilot.Api.Services;

public interface IMcpResearchService
{
    Task<McpToolSession> CreateResearchSessionAsync(CancellationToken cancellationToken = default);
}

public sealed class McpToolSession : IAsyncDisposable
{
    private readonly List<McpClient> _clients;

    public IList<AITool> Tools { get; }

    public McpToolSession(IList<AITool> tools, List<McpClient> clients)
    {
        Tools = tools;
        _clients = clients;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var client in _clients)
        {
            try
            {
                await client.DisposeAsync();
            }
            catch
            {
                // Ignora falhas de encerramento de subprocesso MCP
            }
        }
    }
}

public class McpResearchService : IMcpResearchService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly McpOptions _mcpOptions;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<McpResearchService> _logger;
    private readonly ResiliencePipeline _mcpConnectionPipeline;

    public McpResearchService(
        IOptions<McpOptions> mcpOptions,
        IHttpClientFactory httpClientFactory,
        IHostEnvironment environment,
        ILogger<McpResearchService> logger)
    {
        _mcpOptions = mcpOptions.Value;
        _httpClientFactory = httpClientFactory;
        _environment = environment;
        _logger = logger;

        _mcpConnectionPipeline = new ResiliencePipelineBuilder()
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatio = 0.5,
                SamplingDuration = TimeSpan.FromSeconds(30),
                MinimumThroughput = 3,
                BreakDuration = TimeSpan.FromSeconds(30)
            })
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 2,
                Delay = TimeSpan.FromMilliseconds(500),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true
            })
            .Build();
    }

    public async Task<McpToolSession> CreateResearchSessionAsync(CancellationToken cancellationToken = default)
    {
        var tools = new List<AITool>();
        var clients = new List<McpClient>();
        var hasFetchMcp = false;
        var hasSearchMcp = false;

        var servers = await LoadServerConfigsAsync(cancellationToken);

        foreach (var (name, config) in servers)
        {
            if (!config.Enabled)
            {
                continue;
            }

            try
            {
                var (client, mcpTools) = await _mcpConnectionPipeline.ExecuteAsync(async ct =>
                {
                    McpClient createdClient;
                    if (!string.IsNullOrWhiteSpace(config.Url))
                    {
                        var httpTransport = new HttpClientTransport(new HttpClientTransportOptions
                        {
                            Endpoint = new Uri(config.Url),
                            Name = name
                        });
                        createdClient = await McpClient.CreateAsync(httpTransport, cancellationToken: ct);
                    }
                    else if (!string.IsNullOrWhiteSpace(config.Command))
                    {
                        var (resolvedCommand, resolvedArgs) = ResolveCommandForOs(config.Command, config.Arguments);
                        var stdioTransport = new StdioClientTransport(new StdioClientTransportOptions
                        {
                            Name = name,
                            Command = resolvedCommand,
                            Arguments = resolvedArgs,
                            EnvironmentVariables = config.EnvironmentVariables!
                        });
                        createdClient = await McpClient.CreateAsync(stdioTransport, cancellationToken: ct);
                    }
                    else
                    {
                        throw new InvalidOperationException($"Servidor MCP '{name}' sem Command ou Url.");
                    }

                    var listedTools = await createdClient.ListToolsAsync(cancellationToken: ct);
                    return (createdClient, listedTools);
                }, cancellationToken);

                clients.Add(client);
                foreach (var tool in mcpTools)
                {
                    tools.Add(tool);
                }

                if (name.Contains("fetch", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("playwright", StringComparison.OrdinalIgnoreCase) ||
                    mcpTools.Any(t => t.Name.Contains("fetch", StringComparison.OrdinalIgnoreCase) ||
                                      t.Name.Contains("navigate", StringComparison.OrdinalIgnoreCase)))
                {
                    hasFetchMcp = true;
                }

                if (name.Contains("search", StringComparison.OrdinalIgnoreCase) ||
                    mcpTools.Any(t => t.Name.Contains("search", StringComparison.OrdinalIgnoreCase)))
                {
                    hasSearchMcp = true;
                }

                _logger.LogInformation("Servidor MCP '{ServerName}' conectado com {ToolCount} ferramenta(s).", name, mcpTools.Count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Não foi possível conectar ao servidor MCP '{ServerName}'. Usando fallback local se necessário.", name);
            }
        }

        // Garante ferramentas Fetch e Search mesmo quando servidores MCP externos não estão habilitados.
        if (!hasFetchMcp)
        {
            tools.Add(AIFunctionFactory.Create(FetchWebPageAsync, "mcp_fetch_url",
                "Busca e extrai o texto principal de uma URL (site da empresa ou descrição da vaga)."));
        }

        if (!hasSearchMcp)
        {
            tools.Add(AIFunctionFactory.Create(SearchWebAsync, "mcp_search_web",
                "Pesquisa na web informações sobre a empresa, cultura, produtos e vagas similares no mercado."));
        }

        return new McpToolSession(tools, clients);
    }

    [Description("Faz download do conteúdo textual de uma página web a partir de sua URL.")]
    private async Task<string> FetchWebPageAsync(
        [Description("URL completa da página a ser consultada (https://...)")] string url,
        CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return "URL inválida. Informe uma URL http:// ou https://.";
        }

        try
        {
            var client = _httpClientFactory.CreateClient("McpResearch");
            using var response = await client.GetAsync(uri, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return $"Falha ao acessar {url}: HTTP {(int)response.StatusCode}.";
            }

            var html = await response.Content.ReadAsStringAsync(cancellationToken);
            var text = StripHtml(html);
            return text.Length > 8000 ? text[..8000] : text;
        }
        catch (Exception ex)
        {
            return $"Erro ao consultar {url}: {ex.Message}";
        }
    }

    [Description("Pesquisa na web por empresas, vagas similares e padrões de mercado.")]
    private async Task<string> SearchWebAsync(
        [Description("Termos de busca (ex: 'InComm Payments missão valores produtos' ou 'Senior .NET Engineer remote Brazil')")] string query,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("McpResearch");
            var encoded = Uri.EscapeDataString(query);
            var url = $"https://html.duckduckgo.com/html/?q={encoded}";
            using var response = await client.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return $"Falha na pesquisa para '{query}': HTTP {(int)response.StatusCode}.";
            }

            var html = await response.Content.ReadAsStringAsync(cancellationToken);
            var text = StripHtml(html);
            return text.Length > 6000 ? text[..6000] : text;
        }
        catch (Exception ex)
        {
            return $"Erro ao pesquisar '{query}': {ex.Message}";
        }
    }

    private async Task<Dictionary<string, McpServerConfig>> LoadServerConfigsAsync(CancellationToken cancellationToken)
    {
        var candidatePaths = new[]
        {
            Path.Combine(_environment.ContentRootPath, "mcp-servers.json"),
            Path.Combine(AppContext.BaseDirectory, "mcp-servers.json")
        };

        var filePath = candidatePaths.FirstOrDefault(File.Exists);
        if (filePath is not null)
        {
            try
            {
                var json = await File.ReadAllTextAsync(filePath, cancellationToken);
                var fileConfig = JsonSerializer.Deserialize<McpServersFile>(json, JsonOptions);
                if (fileConfig?.McpServers is { Count: > 0 })
                {
                    _logger.LogInformation("Carregando {Count} servidor(es) MCP de {Path}.", fileConfig.McpServers.Count, filePath);
                    return fileConfig.McpServers.ToDictionary(
                        kvp => kvp.Key,
                        kvp => new McpServerConfig
                        {
                            Enabled = kvp.Value.Enabled,
                            Command = kvp.Value.Command,
                            Arguments = kvp.Value.Args ?? [],
                            EnvironmentVariables = kvp.Value.Env,
                            Url = kvp.Value.Url
                        },
                        StringComparer.OrdinalIgnoreCase);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Falha ao ler {Path}. Usando configuração MCP do appsettings.", filePath);
            }
        }

        return _mcpOptions.Servers;
    }

    private static (string Command, string[] Args) ResolveCommandForOs(string command, string[] args)
    {
        if (OperatingSystem.IsWindows() &&
            (command.Equals("npx", StringComparison.OrdinalIgnoreCase) ||
             command.Equals("npm", StringComparison.OrdinalIgnoreCase)))
        {
            return ("cmd.exe", ["/c", command, .. args]);
        }

        return (command, args);
    }

    private static string StripHtml(string html)
    {
        var withoutScripts = Regex.Replace(html, @"<(script|style)[^>]*>[\s\S]*?</\1>", " ", RegexOptions.IgnoreCase);
        var withoutTags = Regex.Replace(withoutScripts, @"<[^>]+>", " ");
        var decoded = WebUtility.HtmlDecode(withoutTags);
        return Regex.Replace(decoded, @"\s+", " ").Trim();
    }
}
