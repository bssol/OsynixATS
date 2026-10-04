using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Playwright;
using Osynix.Ats.Domain;
namespace Osynix.Ats.Services;
public class ReportService(IConfiguration config,IWebHostEnvironment env)
{
    static string H(string? s)=>WebUtility.HtmlEncode(s??"").Replace("\n","<br>");
    public string Html(IEnumerable<Assessment> records)
    {
        var html=new StringBuilder("<!doctype html><html><head><meta charset='utf-8'><title>Osynix Candidate Assessment Report</title><style>@page{size:A4;margin:16mm}body{font:12px Arial;color:#24324b;margin:0}h1,h2{color:#1c1e66}h1{font-size:23px}h2{font-size:15px;margin-top:22px}.report{break-after:page}.report:last-child{break-after:auto}.kpis{display:flex;gap:10px}.kpi{background:#eff4ff;border:1px solid #dce5f5;padding:12px;flex:1}.kpi b{display:block;font-size:22px;color:#175aff}table{width:100%;border-collapse:collapse;margin-top:12px}th,td{border:1px solid #dde4ef;text-align:left;padding:8px;vertical-align:top}thead{display:table-header-group}tr,.kpi{break-inside:avoid}.muted{color:#667085}.green{background:#e9f8ee}.yellow{background:#fff7dd}.red{background:#fff0ef}.logo{width:165px;height:auto;object-fit:contain}footer{margin-top:20px;border-top:1px solid #ddd;padding-top:10px;font-size:10px}p{line-height:1.6}</style></head><body>");
        string? logo=null;
        var configured=config["Branding:LogoPath"];
        if(!string.IsNullOrWhiteSpace(configured)) {
            var path=Path.GetFullPath(configured,env.ContentRootPath);
            if(File.Exists(path) && new FileInfo(path).Length<2_000_000 && Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg") logo="data:"+(Path.GetExtension(path).ToLowerInvariant()==".png"?"image/png":"image/jpeg")+";base64,"+Convert.ToBase64String(File.ReadAllBytes(path));
        }
        foreach(var a in records) {
            var s=JsonSerializer.Deserialize<AssessmentSnapshot>(a.SnapshotJson)??throw new InvalidOperationException("Missing report snapshot.");
            html.Append("<section class='report'>");
            if(logo is not null) html.Append("<img class='logo' alt='Osynix Group' src='").Append(logo).Append("'>");
            else html.Append("<h2>OSYNIX GROUP</h2><p class='muted'>Draft report — original logo not configured</p>");
            html.Append("<h1>Candidate Assessment Report</h1><p class='muted'>").Append(H(s.Client)).Append(" · ").Append(H(s.PositionTitle)).Append(" · Ref #").Append(H(s.Reference)).Append("</p><h2>").Append(H(s.Profile.Name)).Append("</h2><p>").Append(H(s.Profile.CurrentRole)).Append(" · ").Append(H(s.Profile.CurrentCompany)).Append(" · ").Append(H(s.Profile.Location)).Append("</p><div class='kpis'><div class='kpi'><b>").Append(a.MatchPercent).Append("%</b>ATS match</div><div class='kpi'><b>").Append(H(a.MustHaveFit)).Append("</b>Must-have fit</div><div class='kpi'><b>").Append(a.CoreRoleFit).Append("/10</b>Core role fit</div><div class='kpi'><b>").Append(a.EvidenceStrength).Append("/10</b>Evidence strength</div></div><h2 class='").Append(Choices.Tone(a.Decision)).Append("'>Recommendation: ").Append(H(a.Decision)).Append("</h2>");
            foreach(var (title,text) in new[]{("Executive assessment",s.Summary),("Key strengths",s.Strengths),("Risks and gaps",s.Gaps)}) html.Append("<h2>").Append(title).Append("</h2><p>").Append(H(text)).Append("</p>");
            html.Append("<h2>Requirement-by-requirement evaluation</h2><table><thead><tr><th>Requirement</th><th>Weight</th><th>Score</th><th>Evidence / rationale</th></tr></thead><tbody>");
            foreach(var c in s.Criteria) html.Append("<tr><td>").Append(H(c.Name)).Append(c.Knockout?" (must-have)":"").Append("</td><td>").Append(c.Weight).Append("%</td><td>").Append(c.Score).Append("/10<br>").Append(H(c.Status)).Append("</td><td>").Append(H(c.Found)).Append("<br>").Append(H(c.Rationale)).Append("</td></tr>");
            html.Append("</tbody></table>");
            if(s.Criteria.Count==0) html.Append("<p>Historical import: detailed criterion results were not present in the mapped workbook columns. Original imported rows are retained for reconciliation.</p>");
            html.Append("<h2>Interview focus areas</h2><ul>");
            foreach(var c in s.Criteria.Where(c=>c.Score<6||c.Confidence<6)) html.Append("<li>Verify ").Append(H(c.Name)).Append(" with specific examples and supporting evidence.</li>");
            html.Append("<li>Verify employment dates, responsibilities, and role-relevant credentials.</li></ul><footer>Confidential — Osynix Group. Screening is based on supplied CV evidence and configured job criteria. Findings require human review and verification; this report is not a hiring decision. Candidate phone and email are excluded.</footer></section>");
        }
        return html.Append("</body></html>").ToString();
    }
    public async Task<byte[]> Pdf(string html)
    {
        using var playwright=await Playwright.CreateAsync();
        await using var browser=await playwright.Chromium.LaunchAsync(new(){Headless=true});
        var page=await browser.NewPageAsync();
        // Reports contain only escaped text and embedded local logo bytes; block any network fetch.
        await page.RouteAsync("**/*",route=>route.AbortAsync());
        await page.SetContentAsync(html);
        return await page.PdfAsync(new(){Format="A4",PrintBackground=true,PreferCSSPageSize=true});
    }
}
