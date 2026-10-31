using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Osynix.Ats.Data;
using Osynix.Ats.Domain;
namespace Osynix.Ats.Services;
public class GoogleInterviewEvent {
 public Guid InterviewId {get;set;}
 public string OwnerId {get;set;}="";
 public string GoogleSubject {get;set;}="";
 public string EventId {get;set;}="";
 public string SyncedHash {get;set;}="";
 public string CandidateResponseHash {get;set;}="";
 public string GoogleProposalHash {get;set;}="";
 public string CalendarUrl {get;set;}="";
 public string Error {get;set;}="";
 public bool Deleted {get;set;}
}
public record GoogleMeetConnection(string Subject,string Email,string RefreshToken,string? ClientId=null);
public class GoogleMeetAuthorizationException(string message,string errorCode,int statusCode):InvalidOperationException(message){public string ErrorCode{get;}=errorCode;public int StatusCode{get;}=statusCode;}
public class GoogleMeetService(IDbContextFactory<AtsDbContext> factory,IDataProtectionProvider protection,IConfiguration config,IHttpClientFactory clients,RecruitmentUpdates? updates=null) {
 public const string Scheme="GoogleMeet";
 public const string Scope="https://www.googleapis.com/auth/calendar.events.owned";
 public const string GmailProposalScope="https://www.googleapis.com/auth/gmail.readonly";
 const string TokenName="Connection";
 static readonly SemaphoreSlim SyncGate=new(1,1);
 IDataProtector Protector=>protection.CreateProtector("Osynix.GoogleMeet.Credentials.v1");
  public static (string Id,string Secret) ClientSettings(IConfiguration configuration){
  var id=configuration["GoogleMeet:ClientId"];var secret=configuration["GoogleMeet:ClientSecret"];
  if(string.IsNullOrWhiteSpace(id)&&string.IsNullOrWhiteSpace(secret)){id=configuration["Authentication:Google:ClientId"];secret=configuration["Authentication:Google:ClientSecret"];}
  return(id??"",secret??"");
 }
 public static bool HasClientConfiguration(IConfiguration configuration){var c=ClientSettings(configuration);return !string.IsNullOrWhiteSpace(c.Id)&&!string.IsNullOrWhiteSpace(c.Secret);}
 public bool Configured=>HasClientConfiguration(config);
 public static void Configure(GoogleOptions o,IConfiguration config){
  var client=ClientSettings(config);o.ClientId=client.Id;o.ClientSecret=client.Secret;
  o.CallbackPath="/signin-google-meet";o.SignInScheme="GoogleMeetExternal";
  o.Scope.Add(Scope);o.Scope.Add(GmailProposalScope);o.SaveTokens=false;o.AccessType="offline";o.UsePkce=true;
  o.Events.OnRedirectToAuthorizationEndpoint=c=>{c.Response.Redirect(c.RedirectUri+"&prompt=consent");return Task.CompletedTask;};
  o.Events.OnCreatingTicket=async c=>{
   c.Properties.Items.TryGetValue("owner",out var owner);
   var auth=await c.HttpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);
   if(owner is null||owner!=auth.Principal?.FindFirstValue(ClaimTypes.NameIdentifier))throw new InvalidOperationException("Sign in again before connecting Google Meet.");
   var granted=c.TokenResponse.Response!.RootElement;
   var grantedScopes=granted.TryGetProperty("scope",out var scopes)?(scopes.GetString()??"").Split(' '):[];
   if(!grantedScopes.Contains(Scope)||!grantedScopes.Contains(GmailProposalScope))throw new InvalidOperationException("Google Calendar and Gmail read-only permissions must both be granted.");
   var db=c.HttpContext.RequestServices.GetRequiredService<AtsDbContext>();
      var integrationAccess=await IntegrationEntitlements.Require(db,auth.Principal!);
   if(!c.Properties.Items.TryGetValue("integration-workspace",out var workspace)||workspace!=IntegrationEntitlements.ContextKey(integrationAccess))throw new UnauthorizedAccessException("The active workspace changed. Start the connection again from Settings.");
   await c.HttpContext.RequestServices.GetRequiredService<GoogleMeetService>().Connect(owner,c.Principal!.FindFirstValue(ClaimTypes.NameIdentifier)!,c.User.GetProperty("email").GetString()!,c.RefreshToken);
  };
  o.Events.OnTicketReceived=c=>{c.HandleResponse();c.Response.Redirect("/settings/integrations?provider=google-meet&connected=1");return Task.CompletedTask;};
  o.Events.OnRemoteFailure=c=>{c.HandleResponse();c.Response.Redirect("/settings/integrations?provider=google-meet&error=authorization");return Task.CompletedTask;};
 }
 public async Task<GoogleMeetConnection?> Connection(string owner){await using var db=await factory.CreateDbContextAsync();var row=await db.Set<IdentityUserToken<string>>().FindAsync(owner,Scheme,TokenName);if(row?.Value is null)return null;try{return JsonSerializer.Deserialize<GoogleMeetConnection>(Protector.Unprotect(row.Value));}catch(CryptographicException){return null;}}
 public async Task Connect(string owner,string subject,string email,string? refresh){
  if(string.IsNullOrWhiteSpace(refresh))throw new InvalidOperationException("Reconnect Google Meet and grant Calendar and Gmail read-only access.");
  await using var db=await factory.CreateDbContextAsync();var account=await db.Users.FindAsync(owner);if(account?.Status!="Active")throw new UnauthorizedAccessException();
  if(await db.GoogleInterviewEvents.AnyAsync(e=>e.OwnerId==owner&&!e.Deleted&&e.GoogleSubject!=subject))throw new InvalidOperationException("Reconnect the original Google account while linked interviews exist.");
  var row=await db.Set<IdentityUserToken<string>>().FindAsync(owner,Scheme,TokenName);if(row is null){row=new(){UserId=owner,LoginProvider=Scheme,Name=TokenName};db.Add(row);}row.Value=Protector.Protect(JsonSerializer.Serialize(new GoogleMeetConnection(subject,email,refresh,ClientSettings(config).Id)));
  foreach(var linked in await db.GoogleInterviewEvents.Where(e=>e.OwnerId==owner&&e.GoogleSubject==subject&&!e.Deleted).ToListAsync())if(AuthorizationError(linked.Error))linked.Error="";await db.SaveChangesAsync();
 }
 public async Task Disconnect(string owner){await using var db=await factory.CreateDbContextAsync();if(await db.GoogleInterviewEvents.AnyAsync(e=>e.OwnerId==owner&&!e.Deleted&&db.InterviewBookings.Any(i=>i.Id==e.InterviewId&&i.Status!="Completed")))throw new InvalidOperationException("Cancel and synchronize active interviews before disconnecting Google Meet.");var row=await db.Set<IdentityUserToken<string>>().FindAsync(owner,Scheme,TokenName);if(row is not null){db.Remove(row);await db.SaveChangesAsync();}}
 public async Task Register(AtsDbContext db,InterviewBooking booking,string owner){
  var existing=await db.GoogleInterviewEvents.FindAsync(booking.Id);if(existing is not null&&!existing.Deleted)return;
  var connection=await Connection(owner)??throw new InvalidOperationException("Connect Google Meet before sending this invitation.");
  if(existing is null)db.Add(new GoogleInterviewEvent{InterviewId=booking.Id,OwnerId=owner,GoogleSubject=connection.Subject,EventId="0"+booking.Id.ToString("N")});
  else{existing.OwnerId=owner;existing.GoogleSubject=connection.Subject;existing.EventId="0"+Guid.NewGuid().ToString("N");existing.Deleted=false;existing.SyncedHash="";existing.CandidateResponseHash="";existing.GoogleProposalHash="";existing.CalendarUrl="";existing.Error="";}
 }
 public async Task CheckAuthorization(string owner,string subject){var token=await AccessToken(owner,subject);using var request=new HttpRequestMessage(HttpMethod.Get,"https://gmail.googleapis.com/gmail/v1/users/me/profile");request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);using var response=await clients.CreateClient().SendAsync(request);if(response.IsSuccessStatusCode)return;var reason=GoogleApiErrorReason(await response.Content.ReadAsStringAsync());var code=reason is "insufficientPermissions" or "insufficient_scope"?"gmail_permission_required":reason=="accessNotConfigured"?"gmail_api_disabled":"gmail_api_unavailable";var message=code switch{"gmail_permission_required"=>"Google Meet is connected, but ATS still needs Gmail read-only permission. Reconnect Google Meet and approve the Gmail access.","gmail_api_disabled"=>"Google Meet is connected, but the Gmail API is not enabled in this Google Cloud project. Enable Gmail API, then reconnect Google Meet.",_=>"Google could not verify Gmail access. Check that the Gmail API is enabled and reconnect Google Meet."};throw new GoogleMeetAuthorizationException(message,code,(int)response.StatusCode);}
 static string GoogleApiErrorReason(string body){try{using var json=JsonDocument.Parse(body);return json.RootElement.GetProperty("error").GetProperty("errors")[0].GetProperty("reason").GetString()??"";}catch(Exception e)when(e is JsonException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException){return "";}}
 static bool AuthorizationError(string message)=>message.StartsWith("Google authorization",StringComparison.Ordinal)||message.StartsWith("Google OAuth",StringComparison.Ordinal)||message.StartsWith("Google rejected this connection",StringComparison.Ordinal)||message.StartsWith("Google could not authorize",StringComparison.Ordinal)||message.StartsWith("The Google OAuth client",StringComparison.Ordinal)||message.StartsWith("Reconnect the original Google account",StringComparison.Ordinal)||message.StartsWith("Google Meet client settings",StringComparison.Ordinal)||message.StartsWith("Google Meet is connected, but",StringComparison.Ordinal);
 public async Task VerifyConnection(string owner){
  var connection=await Connection(owner)??throw new InvalidOperationException("Connect Google Meet first.");
  await using var db=await factory.CreateDbContextAsync();var events=await db.GoogleInterviewEvents.Where(e=>e.OwnerId==owner&&e.GoogleSubject==connection.Subject&&!e.Deleted).ToListAsync();
  try{await CheckAuthorization(owner,connection.Subject);foreach(var linked in events.Where(e=>AuthorizationError(e.Error)))linked.Error="";await db.SaveChangesAsync();}
  catch(GoogleMeetAuthorizationException e){foreach(var linked in events)linked.Error=e.Message;await db.SaveChangesAsync();throw;}
 }
 async Task<string> AccessToken(string owner,string subject){
  if(!Configured)throw new InvalidOperationException("Google Meet client settings are missing. Configure the Google OAuth client ID and secret, then restart ATS.");
  var client=ClientSettings(config);var c=await Connection(owner);if(c is null||c.Subject!=subject)throw new InvalidOperationException("Reconnect the original Google account in interview integrations.");
  if(c.ClientId is not null&&c.ClientId!=client.Id)throw new GoogleMeetAuthorizationException("The Google OAuth client has changed since this account was connected. Reconnect Google Meet using the same Google account.","client_changed",0);
  using var response=await clients.CreateClient().PostAsync("https://oauth2.googleapis.com/token",new FormUrlEncodedContent(new Dictionary<string,string>{{"client_id",client.Id},{"client_secret",client.Secret},{"refresh_token",c.RefreshToken},{"grant_type","refresh_token"}}));
    if(!response.IsSuccessStatusCode){
   var error="";try{using var failure=JsonDocument.Parse(await response.Content.ReadAsStringAsync());if(failure.RootElement.TryGetProperty("error",out var code)&&code.ValueKind==JsonValueKind.String)error=code.GetString()??"";}catch(JsonException){}
      var known=error is "invalid_client" or "invalid_grant" or "unauthorized_client" or "invalid_request" or "invalid_scope" or "access_denied" or "temporarily_unavailable" or "server_error";
   var errorCode=known?error:"unexpected_response";
   var message=error switch{
    "invalid_client"=>"Google OAuth client settings were rejected. Check the client ID and secret, then restart ATS.",
    "invalid_grant"=>"Google authorization was revoked, expired, or belongs to a different OAuth client. Reconnect the original Google account and retry.",
    "unauthorized_client"=>"Google rejected this connection for the configured OAuth client. Reconnect Google Meet with the same Google account to authorize the current client.",
    "invalid_request" or "invalid_scope"=>"Google rejected the authorization request. Ask your administrator to check the Google OAuth configuration.",
    "access_denied"=>"Google account access was denied. Reconnect Google Meet and grant Calendar and Gmail read-only access.",
    _ when (int)response.StatusCode>=500||(int)response.StatusCode==429||error is "temporarily_unavailable" or "server_error"=>"Google authorization service is temporarily unavailable. Retry shortly; your existing meeting is unchanged.",
    _=>"Google could not authorize this connection. Reconnect Google Meet; if this continues, ask your administrator to check the OAuth client configuration."
   };
   throw new GoogleMeetAuthorizationException(message,errorCode,(int)response.StatusCode);
  }
  using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync());return json.RootElement.GetProperty("access_token").GetString()!;
 }
 async Task<HttpResponseMessage> Request(HttpMethod method,string path,string token,object? body=null){using var request=new HttpRequestMessage(method,"https://www.googleapis.com/calendar/v3/calendars/primary/events"+path);request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);if(body is not null)request.Content=JsonContent.Create(body);return await clients.CreateClient().SendAsync(request);}
 async Task<HttpResponseMessage> GmailRequest(string path,string token){using var request=new HttpRequestMessage(HttpMethod.Get,"https://gmail.googleapis.com/gmail/v1/users/me/"+path);request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);return await clients.CreateClient().SendAsync(request);}
 static string Hash(object value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
 sealed record GoogleProposal(string Uid,DateTime StartUtc,DateTime EndUtc,string TimeZone,string CandidateEmail,string Comment);
 sealed record GmailCandidateMessage(string Id,string From,List<string> CalendarParts);
 static string DecodeBase64Url(string value){var base64=value.Replace('-','+').Replace('_','/');return Encoding.UTF8.GetString(Convert.FromBase64String(base64.PadRight((base64.Length+3)/4*4,'=')));}
 static void CollectCalendarParts(JsonElement part,List<(string Mime,string Data,string Attachment)> result){if(part.TryGetProperty("mimeType",out var mime)&&part.TryGetProperty("body",out var body)){var kind=mime.GetString()??"";var data=body.TryGetProperty("data",out var raw)?raw.GetString()??"":"";var attachment=body.TryGetProperty("attachmentId",out var aid)?aid.GetString()??"":"";if(kind.StartsWith("text/calendar",StringComparison.OrdinalIgnoreCase)||kind is "text/plain" or "text/html" or "application/ics")result.Add((kind,data,attachment));}if(part.TryGetProperty("parts",out var children)&&children.ValueKind==JsonValueKind.Array)foreach(var child in children.EnumerateArray())CollectCalendarParts(child,result);}
 static string Header(JsonElement payload,string name){if(!payload.TryGetProperty("headers",out var headers)||headers.ValueKind!=JsonValueKind.Array)return "";foreach(var header in headers.EnumerateArray())if(header.TryGetProperty("name",out var n)&&n.GetString()?.Equals(name,StringComparison.OrdinalIgnoreCase)==true&&header.TryGetProperty("value",out var v))return v.GetString()??"";return "";}
 static string Address(string from){var match=Regex.Match(from,"<([^<>]+)>");return (match.Success?match.Groups[1].Value:from).Trim().Trim('"');}
 static string UnfoldCalendar(string value)=>Regex.Replace(value,"\\r?\\n[ \\t]","",RegexOptions.CultureInvariant);
 static (string Name,string Parameters,string Value)? Property(string line){var colon=line.IndexOf(':');if(colon<0)return null;var parts=line[..colon].Split(';');return(parts[0],string.Join(';',parts.Skip(1)),line[(colon+1)..]);}
 static string? Parameter(string parameters,string name){foreach(var part in parameters.Split(';')){var pair=part.Split('=',2);if(pair.Length==2&&pair[0].Equals(name,StringComparison.OrdinalIgnoreCase))return pair[1].Trim('"');}return null;}
 static bool ParseIcsDate(string value,string? tzid,string fallbackZone,out DateTime utc,out string zone){utc=default;zone="";try{if(value.Length==8){if(!DateTime.TryParseExact(value,"yyyyMMdd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var date))return false;zone=tzid??fallbackZone;var localDate=DateTime.SpecifyKind(date,DateTimeKind.Unspecified);var dateZone=TimeZoneInfo.FindSystemTimeZoneById(zone);if(dateZone.IsInvalidTime(localDate)||dateZone.IsAmbiguousTime(localDate))return false;utc=TimeZoneInfo.ConvertTimeToUtc(localDate,dateZone);return true;}var isUtc=value.EndsWith('Z');var format=isUtc?"yyyyMMdd'T'HHmmss'Z'":"yyyyMMdd'T'HHmmss";if(!DateTime.TryParseExact(value,format,CultureInfo.InvariantCulture,DateTimeStyles.None,out var parsed))return false;if(isUtc){utc=DateTime.SpecifyKind(parsed,DateTimeKind.Utc);zone="UTC";return true;}zone=tzid??fallbackZone;var local=DateTime.SpecifyKind(parsed,DateTimeKind.Unspecified);var timeZone=TimeZoneInfo.FindSystemTimeZoneById(zone);if(timeZone.IsInvalidTime(local)||timeZone.IsAmbiguousTime(local))return false;utc=TimeZoneInfo.ConvertTimeToUtc(local,timeZone);return true;}catch(TimeZoneNotFoundException){return false;}catch(InvalidTimeZoneException){return false;}catch(ArgumentException){return false;}}
 static GoogleProposal? ParseProposal(string ics,string fallbackZone){
  var unfolded=UnfoldCalendar(ics);var lines=unfolded.Split(["\r\n","\n"],StringSplitOptions.RemoveEmptyEntries);if(!lines.Any(l=>l.Trim().Equals("METHOD:COUNTER",StringComparison.OrdinalIgnoreCase)))return null;
  string uid="",candidate="",comment="";DateTime? start=null,end=null;string tz="",endZone="";var inEvent=false;
  foreach(var line in lines){if(line.Equals("BEGIN:VEVENT",StringComparison.OrdinalIgnoreCase)){inEvent=true;continue;}if(line.Equals("END:VEVENT",StringComparison.OrdinalIgnoreCase)){break;}if(!inEvent)continue;var property=Property(line);if(property is null)continue;var(name,parameters,value)=property.Value;var v=value.Trim();switch(name.ToUpperInvariant()){
   case "UID":uid=v;break;
   case "ATTENDEE":var mail=Regex.Match(v,@"mailto:([^;?]+)",RegexOptions.IgnoreCase);if(mail.Success)candidate=Uri.UnescapeDataString(mail.Groups[1].Value);break;
   case "COMMENT":comment=v.Replace("\\n"," ",StringComparison.OrdinalIgnoreCase).Replace("\\,",",").Replace("\\;",";").Replace("\\\\","\\");break;
   case "DTSTART":if(ParseIcsDate(v,Parameter(parameters,"TZID"),fallbackZone,out var parsedStart,out var startZone)){start=parsedStart;tz=startZone;}break;
   case "DTEND":if(ParseIcsDate(v,Parameter(parameters,"TZID"),fallbackZone,out var parsedEnd,out var parsedEndZone)){end=parsedEnd;endZone=parsedEndZone;}break;
  }}
  if(uid.Length==0||candidate.Length==0||!start.HasValue)return null;var endUtc=end??start.Value.AddMinutes(30);if(endUtc<=start.Value||endUtc-start.Value>TimeSpan.FromHours(12))return null;return new(uid,start.Value,endUtc,tz.Length>0?tz:endZone,candidate,ReportService.ClientSafeText(comment.Length>2000?comment[..2000]:comment));
 }
 async Task<bool> ImportCalendarProposal(AtsDbContext db,GoogleInterviewEvent map,InterviewBooking i,Assessment a,JsonElement calendarEvent,string token){
  if(i.Status!="Response Requires Review"||i.ResponseSource!="Candidate Via Google Calendar"||!calendarEvent.TryGetProperty("iCalUID",out var uidValue))return false;var uid=uidValue.GetString()??"";if(uid.Length==0)return false;
  if(a.State!="Saved"||a.Stage is "Rejected" or "Hired" or "Offer Sent"||a.Position!.Status is not("Active" or "On Hold")||i.Status is "Cancelled" or "Completed")return false;
  if(await db.InterviewBookings.AnyAsync(b=>b.AssessmentId==i.AssessmentId&&b.CreatedUtc>i.CreatedUtc)||!await db.HiringSubmissions.AnyAsync(s=>s.Id==i.SubmissionId&&!s.Revoked&&db.HiringReviewGrants.Any(g=>g.Id==s.GrantId&&!g.Revoked&&g.ExpiresUtc>DateTime.UtcNow)))return false;
  var query=$"header:X-Google-Calendar-Notification:rsvpProposeNewTime after:{i.CreatedUtc.AddDays(-1).ToString("yyyy/MM/dd",CultureInfo.InvariantCulture)}";
  using var list=await GmailRequest("messages?q="+Uri.EscapeDataString(query)+"&maxResults=25",token);if(!list.IsSuccessStatusCode)throw new InvalidOperationException("Google Gmail could not search for Calendar proposal emails. Check Gmail API access and reconnect Google Meet.");
  using var listed=JsonDocument.Parse(await list.Content.ReadAsStringAsync());if(!listed.RootElement.TryGetProperty("messages",out var messages)||messages.ValueKind!=JsonValueKind.Array)return false;
  foreach(var reference in messages.EnumerateArray()){
   if(!reference.TryGetProperty("id",out var idValue))continue;var messageId=idValue.GetString()??"";if(messageId.Length==0)continue;
   using var response=await GmailRequest("messages/"+Uri.EscapeDataString(messageId)+"?format=full",token);if(!response.IsSuccessStatusCode)continue;
   using var full=JsonDocument.Parse(await response.Content.ReadAsStringAsync());var root=full.RootElement;if(!root.TryGetProperty("payload",out var payload))continue;
   if(!Header(payload,"X-Google-Calendar-Notification").Split([',',' '],StringSplitOptions.RemoveEmptyEntries).Contains("rsvpProposeNewTime",StringComparer.OrdinalIgnoreCase))continue;
   var parts=new List<(string Mime,string Data,string Attachment)>();CollectCalendarParts(payload,parts);
   foreach(var part in parts){
    var content=part.Data;
    if(content.Length==0&&part.Attachment.Length>0){using var attachment=await GmailRequest("messages/"+Uri.EscapeDataString(messageId)+"/attachments/"+Uri.EscapeDataString(part.Attachment),token);if(!attachment.IsSuccessStatusCode)continue;using var attachmentJson=JsonDocument.Parse(await attachment.Content.ReadAsStringAsync());content=attachmentJson.RootElement.TryGetProperty("data",out var data)?data.GetString()??"":"";}
    if(content.Length==0)continue;string text;try{text=DecodeBase64Url(content);}catch(FormatException){continue;}
    var proposal=ParseProposal(text,i.TimeZone);if(proposal is null||!proposal.Uid.Equals(uid,StringComparison.OrdinalIgnoreCase)||!proposal.CandidateEmail.Equals(i.CandidateEmail,StringComparison.OrdinalIgnoreCase))continue;
    var fingerprint=Hash(new{MessageId=messageId,proposal.StartUtc,proposal.EndUtc,Email=proposal.CandidateEmail.ToLowerInvariant()});if(map.GoogleProposalHash==fingerprint)return false;
    if(proposal.StartUtc<=DateTime.UtcNow||proposal.StartUtc>DateTime.UtcNow.AddYears(1))return false;
    map.GoogleProposalHash=fingerprint;i.ProposedStartUtc=proposal.StartUtc;i.ProposedTimeZone=proposal.TimeZone;i.Status="New Time Proposed";i.ResponseSource="Candidate Via Google Calendar";i.ResponseNotes="Candidate proposed "+InterviewService.When(proposal.StartUtc,proposal.TimeZone)+" in Google Calendar."+(proposal.Comment.Length>0?"\nCandidate comment: "+proposal.Comment:"");i.RespondedUtc=DateTime.UtcNow;
    a.InterviewStatus="New Time Proposed";await InterviewService.ApplyGoogleProposal(db,i,a);return true;
   }
  }
  return false;
 }
 public static object EventBody(InterviewBooking i,string title)=>new {summary=title,description="Interview arranged through Osynix ATS. Confirm or propose another time using your ATS invitation.",start=new {dateTime=DateTime.SpecifyKind(i.StartUtc,DateTimeKind.Utc).ToString("O")},end=new {dateTime=DateTime.SpecifyKind(i.StartUtc.AddMinutes(i.DurationMinutes),DateTimeKind.Utc).ToString("O")},attendees=new[]{i.CandidateEmail,i.ManagerEmail}.Where(x=>!string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).Select(email=>new{email}).ToArray(),guestsCanModify=false,guestsCanInviteOthers=false};
 public async Task<string?> Sync(Guid id){
  await SyncGate.WaitAsync();try{
   await using var db=await factory.CreateDbContextAsync();var map=await db.GoogleInterviewEvents.FindAsync(id);if(map is null)return null;
   var i=await db.InterviewBookings.SingleAsync(i=>i.Id==id);var a=await db.Assessments.Include(a=>a.Position).SingleAsync(a=>a.Id==i.AssessmentId);
   var cancel=i.Status=="Cancelled"||i.Mode!="Online"||i.Provider!="Google Meet";
   if(i.Status=="Completed")return null;
   var body=EventBody(i,$"Interview · {a.Position!.Title} · {a.CandidateName}");var desired=Hash(new{cancel,body});
   if(map.Deleted&&cancel)return null;
   try{
    if(map.Deleted&&!cancel)throw new InvalidOperationException("This Google meeting was cancelled. Create a new interview round.");
    var token=await AccessToken(map.OwnerId,map.GoogleSubject);
    var path="/"+map.EventId;const string query="?conferenceDataVersion=1&sendUpdates=all";
    if(cancel){using var deleted=await Request(HttpMethod.Delete,path+"?sendUpdates=all",token);if(!deleted.IsSuccessStatusCode&&deleted.StatusCode is not(HttpStatusCode.NotFound or HttpStatusCode.Gone))throw new InvalidOperationException("Google Calendar cancellation failed. Retry synchronization.");map.Deleted=true;}
    else{
     using var current=await Request(HttpMethod.Get,path,token);
     if(current.IsSuccessStatusCode&&map.SyncedHash==desired&&!string.IsNullOrEmpty(i.MeetingLink)){
      using var remote=JsonDocument.Parse(await current.Content.ReadAsStringAsync());
      var changedResponse=await ReadCandidateResponse(db,map,i,a,remote.RootElement);
      var importedProposal=await ImportCalendarProposal(db,map,i,a,remote.RootElement,token);
      map.Error="";await db.SaveChangesAsync();if(changedResponse||importedProposal)updates?.Publish();return null;
     }
     HttpResponseMessage changed;
     if(current.StatusCode==HttpStatusCode.NotFound){
      var payload=JsonSerializer.SerializeToNode(body)!;payload["id"]=map.EventId;payload["conferenceData"]=JsonSerializer.SerializeToNode(new{createRequest=new{requestId=map.EventId,conferenceSolutionKey=new{type="hangoutsMeet"}}});
      changed=await Request(HttpMethod.Post,query,token,payload);
      if(changed.StatusCode==HttpStatusCode.Conflict){changed.Dispose();changed=await Request(HttpMethod.Get,path,token);}
     }else if(current.IsSuccessStatusCode){
      var patch=JsonSerializer.SerializeToNode(body)!;
      using var existing=JsonDocument.Parse(await current.Content.ReadAsStringAsync());
      if(existing.RootElement.TryGetProperty("attendees",out var guests)&&guests.ValueKind==JsonValueKind.Array){
       var currentEmails=guests.EnumerateArray().Where(g=>g.TryGetProperty("email",out _)).Select(g=>g.GetProperty("email").GetString()??"").ToHashSet(StringComparer.OrdinalIgnoreCase);
       var desiredEmails=new[]{i.CandidateEmail,i.ManagerEmail}.Where(e=>!string.IsNullOrWhiteSpace(e)).ToHashSet(StringComparer.OrdinalIgnoreCase);
       if(currentEmails.SetEquals(desiredEmails))patch.AsObject().Remove("attendees");
      }
      changed=await Request(map.SyncedHash==desired?HttpMethod.Get:HttpMethod.Patch,path+(map.SyncedHash==desired?"":query),token,map.SyncedHash==desired?null:patch);
     }
     else throw new InvalidOperationException("Google Calendar is unavailable or permission was revoked. Reconnect and retry.");
     using(changed){if(!changed.IsSuccessStatusCode)throw new InvalidOperationException("Google Calendar could not save the meeting. Check calendar permissions and retry.");using var json=JsonDocument.Parse(await changed.Content.ReadAsStringAsync());
      map.CandidateResponseHash=CandidateResponseHash(json.RootElement,i.CandidateEmail);
      UpdateCalendarUrl(map,json.RootElement);
      map.SyncedHash=desired;var link=MeetingLink(json.RootElement);if(link is null)throw new InvalidOperationException("Google is preparing the Meet link. Retry synchronization shortly.");
      i.MeetingLink=link;
      if(i.Status=="Response Requires Review")await ImportCalendarProposal(db,map,i,a,json.RootElement,token);
      foreach(var mail in await db.InterviewDeliveries.Where(m=>m.InterviewId==id&&(m.Status=="Pending"||m.Status=="Failed")).ToListAsync())mail.Body=mail.Body.Replace("Meeting: \n","Meeting: "+link+"\n");
     }
    }
    map.SyncedHash=desired;map.Error="";await db.SaveChangesAsync();return null;
   }catch(Exception e) when(e is HttpRequestException or TaskCanceledException or InvalidOperationException or JsonException){map.Error=e is InvalidOperationException?e.Message:"Google Calendar is temporarily unavailable. Retry synchronization.";await db.SaveChangesAsync();return map.Error;}
  }finally{SyncGate.Release();}
 }
  static (string Status,string Comment)? CandidateResponse(JsonElement e,string email){
  if(!e.TryGetProperty("attendees",out var attendees)||attendees.ValueKind!=JsonValueKind.Array)return null;
  foreach(var attendee in attendees.EnumerateArray()){
   if(!attendee.TryGetProperty("email",out var address)||!string.Equals(address.GetString(),email,StringComparison.OrdinalIgnoreCase))continue;
   if(attendee.TryGetProperty("organizer",out var organizer)&&organizer.ValueKind==JsonValueKind.True)return null;
   var status=attendee.TryGetProperty("responseStatus",out var r)?r.GetString()??"needsAction":"needsAction";
   var comment=attendee.TryGetProperty("comment",out var c)?c.GetString()??"":"";
   return(status,ReportService.ClientSafeText(comment.Length>2000?comment[..2000]:comment));
  }return null;
 }
 static string CandidateResponseHash(JsonElement e,string email)=>CandidateResponse(e,email) is { } r?Hash(new{Email=email.ToLowerInvariant(),r.Status,r.Comment}):"";
 static void UpdateCalendarUrl(GoogleInterviewEvent map,JsonElement e){if(e.TryGetProperty("htmlLink",out var link)&&Uri.TryCreate(link.GetString(),UriKind.Absolute,out var uri)&&uri.Scheme=="https"&&uri.UserInfo.Length==0&&(uri.Host=="www.google.com"||uri.Host=="calendar.google.com"))map.CalendarUrl=uri.AbsoluteUri;}
 async Task<bool> ReadCandidateResponse(AtsDbContext db,GoogleInterviewEvent map,InterviewBooking i,Assessment a,JsonElement remote){
  UpdateCalendarUrl(map,remote);
  var response=CandidateResponse(remote,i.CandidateEmail);if(response is null)return false;
  var fingerprint=CandidateResponseHash(remote,i.CandidateEmail);if(map.CandidateResponseHash==fingerprint)return false;
  map.CandidateResponseHash=fingerprint;
  if(response.Value.Status is not("accepted" or "declined" or "tentative"))return false;
  if(i.Status is "Cancelled" or "Completed"||a.State!="Saved"||a.Stage is "Rejected" or "Hired" or "Offer Sent"||a.Position!.Status is not("Active" or "On Hold"))return false;
  if(await db.InterviewBookings.AnyAsync(b=>b.AssessmentId==i.AssessmentId&&b.CreatedUtc>i.CreatedUtc))return false;
  if(!await db.HiringSubmissions.AnyAsync(s=>s.Id==i.SubmissionId&&!s.Revoked&&db.HiringReviewGrants.Any(g=>g.Id==s.GrantId&&!g.Revoked&&g.ExpiresUtc>DateTime.UtcNow)))return false;
  await InterviewService.ApplyGoogleResponse(db,i,a,response.Value.Status,response.Value.Comment);return true;
 }
 public async Task<string> CalendarLink(Guid id){await using var db=await factory.CreateDbContextAsync();return (await db.GoogleInterviewEvents.FindAsync(id))?.CalendarUrl??"";}
 public static string? MeetingLink(JsonElement e){
  static string? Valid(JsonElement value)=>value.ValueKind==JsonValueKind.String&&Uri.TryCreate(value.GetString(),UriKind.Absolute,out var uri)&&uri.Scheme=="https"&&uri.Host=="meet.google.com"?uri.AbsoluteUri:null;
  if(e.TryGetProperty("hangoutLink",out var link)&&Valid(link) is string direct)return direct;
  if(e.TryGetProperty("conferenceData",out var conference)&&conference.ValueKind==JsonValueKind.Object&&conference.TryGetProperty("entryPoints",out var entries)&&entries.ValueKind==JsonValueKind.Array)
   foreach(var entry in entries.EnumerateArray())if(entry.TryGetProperty("entryPointType",out var type)&&type.GetString()=="video"&&entry.TryGetProperty("uri",out var url)&&Valid(url) is string video)return video;
  return null;
 }
 public async Task<string?> Status(Guid id){await using var db=await factory.CreateDbContextAsync();return (await db.GoogleInterviewEvents.FindAsync(id))?.Error;}
}
public class GoogleMeetWorker(IServiceScopeFactory scopes,ILogger<GoogleMeetWorker> logger):BackgroundService {
 protected override async Task ExecuteAsync(CancellationToken stoppingToken){using var timer=new PeriodicTimer(TimeSpan.FromMinutes(1));while(await timer.WaitForNextTickAsync(stoppingToken)){try{using var scope=scopes.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AtsDbContext>();var ids=await db.GoogleInterviewEvents.Where(e=>db.InterviewBookings.Any(i=>i.Id==e.InterviewId&&i.Status!="Completed")&&(!e.Deleted||db.InterviewDeliveries.Any(m=>m.InterviewId==e.InterviewId&&(m.Status=="Pending"||m.Status=="Failed")))).Select(e=>e.InterviewId).ToListAsync(stoppingToken);foreach(var id in ids){if(stoppingToken.IsCancellationRequested)break;await scope.ServiceProvider.GetRequiredService<InterviewService>().FlushQueued(id);}}catch(Exception e) when(!stoppingToken.IsCancellationRequested){logger.LogWarning("Google Meet synchronization cycle failed ({Type}).",e.GetType().Name);}}}
}
