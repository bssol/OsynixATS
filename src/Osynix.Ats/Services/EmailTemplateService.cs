using System.Net;
using System.Security.Claims;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using Osynix.Ats.Data;
using Osynix.Ats.Domain;
namespace Osynix.Ats.Services;

public class EmailTemplateService(IDbContextFactory<AtsDbContext> factory,AuthenticationStateProvider auth,Access access)
{
    async Task<(Guid? Workspace,string User)> Scope(bool write=false)
    {
        var principal=(await auth.GetAuthenticationStateAsync()).User;
        if(principal.IsInRole("Admin")||principal.IsInRole("SubAdmin")){
            await access.RequireAdminPermission(write?"admin.subscriptions.manage":"admin.workspaces.view");
            return(null,principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
        }
        await access.RequirePermission("EmailTemplates.Manage");
        return(await access.WorkspaceId(),principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
    }
    public async Task<List<EmailTemplate>> List()
    {
        var scope=await Scope();await using var db=await factory.CreateDbContextAsync();
        return await Library(db,scope.Workspace,scope.Workspace is null);
    }
    internal static async Task<List<EmailTemplate>> Library(AtsDbContext db,Guid? workspace,bool system=false)
    {
        var items=EmailTemplateCatalog.All().ToDictionary(t=>t.Key);
        var saved=await db.EmailTemplates.AsNoTracking().Where(t=>t.WorkspaceId==null||(workspace!=null&&t.WorkspaceId==workspace)).ToListAsync();
        foreach(var template in saved.OrderBy(t=>t.WorkspaceId.HasValue))items[template.Key]=template;
        return items.Values.Where(t=>system||t.Recipient!="System").OrderBy(t=>t.Name).ToList();
    }
    public async Task Save(EmailTemplate input)
    {
        var scope=await Scope(true);
        var defaults=EmailTemplateCatalog.All();var builtIn=defaults.SingleOrDefault(t=>t.Key==input.Key);
        if(input.Key.Length==0){input.Key="custom-"+Guid.NewGuid().ToString("N");input.Custom=true;}
        if(input.Key.Length>100||input.Name.Trim().Length is <1 or >160||input.Subject.Trim().Length is <1 or >500||input.Subject.Contains('\r')||input.Subject.Contains('\n'))throw new InvalidOperationException("Enter A Template Name And A Subject Up To 500 Characters.");
        if(!new[]{"Candidate","Client / Hiring Manager","Employer Team","System"}.Contains(input.Recipient))throw new InvalidOperationException("Choose A Valid Recipient.");
        if(scope.Workspace is not null&&(input.Recipient=="System"||builtIn?.Recipient=="System"))throw new UnauthorizedAccessException("Security Templates Are Managed By Admin.");
        if(builtIn is not null&&input.Recipient!=builtIn.Recipient)throw new InvalidOperationException("The Recipient For A Built-In Template Cannot Be Changed.");
        if(input.BodyHtml.Length>30000)throw new InvalidOperationException("Keep The Message Under 30,000 Characters.");
        var html=TemplateHtml.Clean(input.BodyHtml);
        if(string.IsNullOrWhiteSpace(TemplateHtml.Text(html)))throw new InvalidOperationException("Enter A Message.");
        foreach(Match match in Regex.Matches(input.Subject+html,@"\{\{(\w+)\}\}"))if(!EmailTemplateCatalog.Fields.Contains(match.Groups[1].Value))throw new InvalidOperationException("Unknown Merge Field: "+match.Value);
        foreach(var field in input.Key switch{
            "email-verification"=>new[]{"VerificationCode"},"password-reset"=>["PasswordResetLink"],
            "profiles-review"=>["ReviewLink"],"team-invitation"=>["InvitationLink"],
            "interview-invitation"=>["InterviewScheduleAndJoiningDetails","CandidateResponseLink"],_=>Array.Empty<string>()})
            if(!html.Contains("{{"+field+"}}"))throw new InvalidOperationException("Keep The Required Field {{"+field+"}} In The Message.");
        await using var db=await factory.CreateDbContextAsync();
        var row=await db.EmailTemplates.SingleOrDefaultAsync(t=>t.WorkspaceId==scope.Workspace&&t.Key==input.Key);
        if(row is null){if(builtIn is null&&!input.Key.StartsWith("custom-"))throw new InvalidOperationException("Template Not Found.");row=new(){WorkspaceId=scope.Workspace,Key=input.Key,Custom=builtIn is null};db.EmailTemplates.Add(row);}
        else if(row.Revision!=input.Revision)throw new DbUpdateConcurrencyException("The Template Changed. Reload It Before Saving.");
        row.Name=input.Name.Trim();row.Subject=input.Subject.Trim();row.Recipient=input.Recipient;row.BodyHtml=html;
        AtsService.Audit(db,scope.User,"Email template saved",row.Id.ToString(),row.Name+(scope.Workspace is null?" · Platform Default":" · Workspace Template"));
        await db.SaveChangesAsync();
    }
    public async Task Reset(string key,Guid revision)
    {
        var scope=await Scope(true);await using var db=await factory.CreateDbContextAsync();
        var row=await db.EmailTemplates.SingleOrDefaultAsync(t=>t.Key==key&&t.WorkspaceId==scope.Workspace);
        if(row is null)return;if(row.Revision!=revision)throw new DbUpdateConcurrencyException("The Template Changed. Reload Before Resetting.");
        db.EmailTemplates.Remove(row);AtsService.Audit(db,scope.User,"Email template reset",row.Id.ToString(),row.Name);await db.SaveChangesAsync();
    }
    public static async Task Send(IDbContextFactory<AtsDbContext> factory,IAccountEmail mail,Guid? workspace,string key,string address,string subject,string body,Dictionary<string,string> fields)
    {
        await using var db=await factory.CreateDbContextAsync();
        if(workspace is Guid id&&!await db.EmployerWorkspaces.AnyAsync(w=>w.Id==id&&w.EmailTemplatesEnabled)){await mail.Send(address,subject,body);return;}
        // Recommended templates are available for composing; only existing send actions invoke this method.
        var template=(await Library(db,workspace,true)).SingleOrDefault(t=>t.Key==key);
        if(template is null){await mail.Send(address,subject,body);return;}
        string Merge(string text,bool html)=>Regex.Replace(text,@"\{\{(\w+)\}\}",m=>fields.TryGetValue(m.Groups[1].Value,out var value)?(html?WebUtility.HtmlEncode(value).Replace("\n","<br />"):value):m.Value);
        var title=Merge(template.Subject,false);var content=Merge(template.BodyHtml,true);
        if(Regex.IsMatch(title+content,@"\{\{\w+\}\}")){await mail.Send(address,subject,body);return;}
        if(title.Contains('\r')||title.Contains('\n'))throw new InvalidOperationException("Invalid Template Subject.");
        await mail.SendHtml(address,title,content);
    }
}

public static class TemplateHtml
{
    static readonly HashSet<string> Tags=["p","div","span","br","strong","b","em","i","u","s","ul","ol","li","h2","h3","blockquote","a"];
    public static string Clean(string html)
    {
        if(html.Length>30000)throw new InvalidOperationException("Message Is Too Long.");
        html=Regex.Replace(html,@"&([a-zA-Z][a-zA-Z0-9]+);",m=>m.Value is "&amp;" or "&lt;" or "&gt;" or "&quot;" or "&apos;"?m.Value:WebUtility.HtmlEncode(WebUtility.HtmlDecode(m.Value)));
        html=Regex.Replace(html,@"<br\s*/?>","<br />",RegexOptions.IgnoreCase);
        try{
            using var reader=XmlReader.Create(new StringReader("<root>"+html+"</root>"),new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=40000});
            var root=XElement.Load(reader,LoadOptions.PreserveWhitespace);
            foreach(var element in root.Descendants().ToList()){
                var tag=element.Name.LocalName.ToLowerInvariant();
                if(tag is "script" or "style" or "iframe" or "object" or "svg" or "math"){element.Remove();continue;}
                if(!Tags.Contains(tag)){element.ReplaceWith(element.Nodes());continue;}
                element.Name=tag;var href=element.Attribute("href")?.Value;var style=element.Attribute("style")?.Value??"";element.RemoveAttributes();
                if(tag=="a"){
                    var token=Regex.Match(href??"",@"^\{\{(\w+Link)\}\}$");
                    if(token.Success&&EmailTemplateCatalog.Fields.Contains(token.Groups[1].Value))element.SetAttributeValue("href",href);
                    else if(Uri.TryCreate(href,UriKind.Absolute,out var uri)&&uri.Scheme is "https" or "http" or "mailto")element.SetAttributeValue("href",uri.AbsoluteUri);
                    if(element.Attribute("href") is not null)element.SetAttributeValue("rel","noopener noreferrer");
                }
                var alignment=Regex.Match(style,@"(?:^|;)\s*text-align\s*:\s*(left|center|right)\s*(?:;|$)",RegexOptions.IgnoreCase);
                if(alignment.Success)element.SetAttributeValue("style","text-align:"+alignment.Groups[1].Value.ToLowerInvariant());
            }
            return string.Concat(root.Nodes().Select(n=>n.ToString(SaveOptions.DisableFormatting)));
        }catch(XmlException){throw new InvalidOperationException("The Message Contains Invalid Formatting. Paste As Plain Text And Try Again.");}
    }
    public static string Text(string html)=>WebUtility.HtmlDecode(Regex.Replace(Regex.Replace(html,@"</(?:p|div|li|h2|h3)>|<br\s*/?>","\n",RegexOptions.IgnoreCase),"<[^>]*>",""));
}
