namespace CareerPilot.Api.Services;

public interface IRequestLanguageProvider
{
    string GetLanguage();
    bool IsEnglish();
}

public class RequestLanguageProvider : IRequestLanguageProvider
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public RequestLanguageProvider(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public string GetLanguage()
    {
        var header = _httpContextAccessor.HttpContext?.Request.Headers.AcceptLanguage.ToString();
        if (!string.IsNullOrWhiteSpace(header) &&
            header.StartsWith("en", StringComparison.OrdinalIgnoreCase))
        {
            return "en-US";
        }

        return "pt-BR";
    }

    public bool IsEnglish() =>
        GetLanguage().StartsWith("en", StringComparison.OrdinalIgnoreCase);
}
