namespace Osynix.Ats.Domain;

public static class CandidateLinks
{
    public static string NormalizeLinkedIn(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var text = value.Trim();
        if (!text.StartsWith("https://", StringComparison.OrdinalIgnoreCase) && !text.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) text = "https://" + text;
        return Uri.TryCreate(text, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && (uri.Host.Equals("linkedin.com", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".linkedin.com", StringComparison.OrdinalIgnoreCase))
            && uri.AbsolutePath.StartsWith("/in/", StringComparison.OrdinalIgnoreCase)
            ? uri.ToString()
            : "";
    }
}
