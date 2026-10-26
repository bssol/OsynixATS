using System.Net;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using Osynix.Ats.Domain;

namespace Osynix.Ats.Services;

public record JdGenerationBrief(
    string Title,
    string Industry,
    string Seniority,
    string Location,
    string WorkArrangement,
    string EmploymentType,
    string RoleOutcome,
    string AdditionalContext = "");

public record JdTemplateSection(string Key, string Heading, string Content, bool NeedsAttention = false);

public static class JdTemplateFormatter
{
    public static readonly (string Key, string Heading)[] Sections =
    [
        ("summary", "Role Summary"),
        ("responsibilities", "Roles and Responsibilities"),
        ("requirements", "Requirements · Required"),
        ("preferred", "Requirements · Preferred"),
        ("skills", "Skills"),
        ("tools", "Tools and Technologies"),
        ("success", "Success in the Role"),
        ("working", "Working Conditions"),
        ("compensation", "Compensation and Benefits"),
        ("organization", "About the Organization"),
        ("equalOpportunity", "Equal Opportunity")
    ];

    public static List<JdTemplateSection> Complete(IEnumerable<JdTemplateSection>? sections, IEnumerable<string>? attention)
    {
        var supplied = (sections ?? []).GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var flags = (attention ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Sections.Select(definition =>
        {
            supplied.TryGetValue(definition.Key, out var section);
            var content = section?.Content?.Trim() ?? "";
            var scoringSection = definition.Key is "responsibilities" or "requirements" or "skills";
            var needsAttention = scoringSection && (flags.Contains(definition.Key) || section?.NeedsAttention == true || string.IsNullOrWhiteSpace(content));
            return new JdTemplateSection(definition.Key, definition.Heading, content, needsAttention);
        }).ToList();
    }

    public static string ToHtml(IEnumerable<JdTemplateSection> sections)
    {
        var html = new System.Text.StringBuilder();
        foreach (var section in sections)
        {
            html.Append("<section class=\"jd-template-section");
            if (section.NeedsAttention) html.Append(" attention");
            html.Append("\" data-section-key=\"").Append(section.Key).Append("\"><h2>")
                .Append(HtmlEncoder.Default.Encode(section.Heading));
            if (section.NeedsAttention) html.Append(" <span class=\"jd-attention-label\" contenteditable=\"false\">Needs your review</span>");
            html.Append("</h2>");
            html.Append(ContentToHtml(section.Content));
            html.Append("</section>");
        }
        return html.ToString();
    }

    public static string ToPlainText(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return "";
        var text = Regex.Replace(html, "<span\\b[^>]*class=\"jd-attention-label\"[^>]*>.*?</span>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        text = Regex.Replace(text, "</(h[1-6]|p|section|div|li)>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, "<br\\s*/?>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, "<[^>]+>", " ");
        text = WebUtility.HtmlDecode(text).Replace('\u00a0', ' ');
        return string.Join('\n', text.Split('\n').Select(line => Regex.Replace(line, "[ \\t]+", " ").Trim()).Where(line => line.Length > 0));
    }

    public static string SanitizeHtml(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return "";
        html = Regex.Replace(html, "<(script|style|iframe|object|svg|math)\\b[^>]*>.*?</\\1\\s*>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        var output = new System.Text.StringBuilder();
        foreach (Match token in Regex.Matches(html, "<[^>]*>|[^<]+"))
        {
            var value = token.Value;
            if (!value.StartsWith('<'))
            {
                output.Append(HtmlEncoder.Default.Encode(WebUtility.HtmlDecode(value)));
                continue;
            }
            var match = Regex.Match(value, "^<\\s*(/?)\\s*([a-zA-Z0-9]+)");
            if (!match.Success) continue;
            var closing = match.Groups[1].Value == "/";
            var tag = match.Groups[2].Value.ToLowerInvariant();
            if (tag is not ("section" or "h2" or "h3" or "p" or "div" or "ul" or "ol" or "li" or "strong" or "b" or "em" or "i" or "br" or "span")) continue;
            if (closing) { if (tag != "br") output.Append("</").Append(tag).Append('>'); continue; }
            output.Append('<').Append(tag);
            if (tag == "section")
            {
                var key = Regex.Match(value, "data-section-key=\"(summary|responsibilities|requirements|preferred|skills|tools|success|working|compensation|organization|equalOpportunity)\"", RegexOptions.IgnoreCase);
                if (key.Success)
                {
                    var attention = Regex.IsMatch(value, "class=\"[^\"]*\\battention\\b", RegexOptions.IgnoreCase);
                    output.Append(" class=\"jd-template-section").Append(attention ? " attention" : "").Append("\" data-section-key=\"").Append(key.Groups[1].Value).Append('"');
                }
            }
            else if (tag == "span" && Regex.IsMatch(value, "class=\"jd-attention-label\"", RegexOptions.IgnoreCase))
                output.Append(" class=\"jd-attention-label\" contenteditable=\"false\"");
            output.Append(tag == "br" ? "/>" : ">");
        }
        return output.ToString();
    }

    static string ContentToHtml(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return "";
        var lines = content.Replace("\r", "").Split('\n').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
        var html = new System.Text.StringBuilder();
        var inList = false;
        foreach (var line in lines)
        {
            var isBullet = Regex.IsMatch(line, "^(?:[-*•]|\\d+[.)])\\s+");
            if (isBullet)
            {
                if (!inList) { html.Append("<ul>"); inList = true; }
                html.Append("<li>").Append(HtmlEncoder.Default.Encode(Regex.Replace(line, "^(?:[-*•]|\\d+[.)])\\s+", ""))).Append("</li>");
            }
            else
            {
                if (inList) { html.Append("</ul>"); inList = false; }
                html.Append("<p>").Append(HtmlEncoder.Default.Encode(line)).Append("</p>");
            }
        }
        if (inList) html.Append("</ul>");
        return html.ToString();
    }
}
