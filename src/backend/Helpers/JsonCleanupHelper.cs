namespace CareerPilot.Api.Helpers;

public static class JsonCleanupHelper
{
    /// <summary>
    /// Remove cercas markdown (```json ... ```) e recorta texto introdutório/final
    /// preservando apenas o bloco JSON principal ({...} ou [...]).
    /// </summary>
    public static string StripMarkdownCodeFence(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var trimmed = raw.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewLine = trimmed.IndexOf('\n');
            if (firstNewLine >= 0)
            {
                trimmed = trimmed[(firstNewLine + 1)..];
            }

            var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (lastFence >= 0)
            {
                trimmed = trimmed[..lastFence];
            }

            trimmed = trimmed.Trim();
        }

        var firstObj = trimmed.IndexOf('{');
        var firstArr = trimmed.IndexOf('[');
        var startIdx = (firstObj, firstArr) switch
        {
            (>= 0, >= 0) => Math.Min(firstObj, firstArr),
            (>= 0, < 0) => firstObj,
            (< 0, >= 0) => firstArr,
            _ => -1
        };

        if (startIdx < 0)
        {
            return trimmed;
        }

        var expectedClose = trimmed[startIdx] == '{' ? '}' : ']';
        var endIdx = trimmed.LastIndexOf(expectedClose);
        if (endIdx > startIdx)
        {
            return trimmed[startIdx..(endIdx + 1)].Trim();
        }

        return trimmed[startIdx..].Trim();
    }
}
