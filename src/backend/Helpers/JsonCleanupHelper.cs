namespace CareerPilot.Api.Helpers;

public static class JsonCleanupHelper
{
    /// <summary>
    /// Remove cercas markdown (```json ... ```) caso o modelo retorne JSON formatado em bloco de código.
    /// </summary>
    public static string StripMarkdownCodeFence(string raw)
    {
        var trimmed = raw.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return trimmed;
        }

        var firstNewLine = trimmed.IndexOf('\n');
        if (firstNewLine < 0)
        {
            return trimmed.Trim('`').Trim();
        }

        var withoutOpening = trimmed[(firstNewLine + 1)..];
        var lastFence = withoutOpening.LastIndexOf("```", StringComparison.Ordinal);
        if (lastFence >= 0)
        {
            withoutOpening = withoutOpening[..lastFence];
        }

        return withoutOpening.Trim();
    }
}
