using System.Globalization;

using System.Security.Cryptography;

using System.Text;

using System.Text.Json;

using Microsoft.AspNetCore.DataProtection;

using Microsoft.AspNetCore.WebUtilities;

using Microsoft.EntityFrameworkCore;

using Osynix.Ats.Data;

using Osynix.Ats.Domain;

namespace Osynix.Ats.Services;

public record HiringFeedback(Guid SubmissionId,Guid Revision,Guid ApplicationRevision,string Status,string Remarks,string? LocalStart,string TimeZone,string Mode,int DurationMinutes,string Location,Guid? InterviewRevision=null,bool InterviewCompleted=false);

public record ForwardResult(string Link,string Message);
public record ClientReviewLink(string? Link,string Status,DateTime? ExpiresUtc,int Profiles);

public record HiringRow(Guid Id,Guid PositionId,string Reference,string PositionTitle,string CandidateName,int? MatchPercent,string Decision,string Stage,string InterviewStatus,string ClientStatus,string Feedback,string Remarks,DateTime SubmittedUtc,DateTime? FeedbackUtc,DateTime? AvailabilityStartUtc,int DurationMinutes,string TimeZone,string InterviewMode,string InterviewLocation,Guid Revision,Guid ApplicationRevision,string LifecycleQueue,string MustHaveFit,string RecruiterResponse="",DateTime? RecruiterResponseUtc=null,InterviewSummary? Interview=null){
 public string ReviewAction=>Stage is "Hired" or "Offer Sent"?"View Outcome":Interview?.Status is "Confirmed" or "Declined" or "New Time Proposed" or "Completed"?"Evaluate":"Review";
}

public record RecruiterFeedback(Guid Id,Guid AssessmentId,string ContactName,string Feedback,string Remarks,DateTime? AvailabilityStartUtc,int DurationMinutes,string TimeZone,string Mode,string Location,string Delivery);

public class HiringReviewService(IDbContextFactory<AtsDbContext> factory,Access access,IDataProtectionProvider protection,IConfiguration config,IAccountEmail email,RecruitmentUpdates updates){

 public const string CookieName="Osynix.HiringReview";

 public static readonly string[] FeedbackChoices=["Shortlist","Request Interview","More Information Required","Hold","Reject","Recommend Hire","Send Offer"];

 readonly IDataProtector tokenProtector=protection.CreateProtector("Osynix.HiringReview.Token.v1");

 readonly IDataProtector cookieProtector=protection.CreateProtector("Osynix.HiringReview.Cookie.v1");

 public bool CanShareExternally=>Uri.TryCreate(config["Review:PublicBaseUrl"],UriKind.Absolute,out var publicUri)&&publicUri.Scheme=="https"&&!publicUri.IsLoopback;
 public bool CanEmail=>Uri.TryCreate(config["Review:PublicBaseUrl"],UriKind.Absolute,out var uri)&&uri.Scheme=="https"&&!uri.IsLoopback&&email.Configured;

 string BaseUrl=>Uri.TryCreate(config["Review:PublicBaseUrl"],UriKind.Absolute,out var uri)&&uri.Scheme=="https"?uri.GetLeftPart(UriPartial.Authority):"http://localhost:5080";

 static string Hash(string token)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

 static Assessment Shared(Assessment a)=>new(){SnapshotJson=a.SnapshotJson,MatchPercent=a.MatchPercent,MustHaveFit=a.MustHaveFit,CoreRoleFit=a.CoreRoleFit,EvidenceStrength=a.EvidenceStrength,Decision=a.Decision};

 public async Task<List<Assessment>> Preview(IEnumerable<Guid> ids){

  var list=ids.Distinct().ToList();if(list.Count is <1 or >50)throw new InvalidOperationException("Select 1–50 saved profiles.");

  foreach(var id in list)await access.RequireApplication(id);

  await using var db=await factory.CreateDbContextAsync();

  var records=await db.Assessments.Include(x=>x.Position).Where(a=>list.Contains(a.Id)&&a.State=="Saved").AsNoTracking().ToListAsync();

  if(records.Count!=list.Count)throw new InvalidOperationException("Only saved assessments can be forwarded.");

  if(records.Any(x=>string.IsNullOrWhiteSpace(x.SnapshotJson)))throw new InvalidOperationException("Each profile needs a saved assessment report.");

  return records.Select(Shared).ToList();

 }

 public Task<ForwardResult> Forward(Guid contactId,IEnumerable<Guid> ids,string message,bool sendEmail)=>ForwardCore(contactId,ids,message,null,sendEmail);
 public Task<ForwardResult> Forward(Guid? contactId,IEnumerable<Guid> ids,string message,string? recipientEmail)=>ForwardCore(contactId,ids,message,recipientEmail,null);
 async Task<ForwardResult> ForwardCore(Guid? contactId,IEnumerable<Guid> ids,string message,string? recipientEmail,bool? emailRequested){

  var user=await access.Require();var uid=await access.UserId();var list=ids.Distinct().ToList();await Preview(list);

  if(message.Length>5000)throw new InvalidOperationException("Keep the message within 5,000 characters.");

  await using var db=await factory.CreateDbContextAsync();
  var apps=await db.Assessments.Include(a=>a.Position).Where(a=>list.Contains(a.Id)).ToListAsync();
  if(apps.Select(a=>a.PositionId).Distinct().Count()!=1||apps.Any(a=>a.Position?.ClientId is null||a.Position.Status!="Active"||a.State!="Saved"||a.Stage=="Hired"||a.ClientStatus=="Hired"||a.Stage=="Rejected"))throw new InvalidOperationException("Select saved profiles from one active position linked to a client.");
  var clientId=apps[0].Position!.ClientId!.Value;
  var client=await db.Clients.SingleAsync(c=>c.Id==clientId);
  ClientContact? contact=null;
  if(contactId is Guid cid){
   contact=await db.ClientContacts.SingleOrDefaultAsync(c=>c.Id==cid&&c.ClientId==clientId)??throw new InvalidOperationException("Choose a contact linked to this position's client.");
  }
  var address=(recipientEmail??contact?.Email??"").Trim();
  var hasEmail=new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(address);
  if(address.Length>320||(address.Length>0&&!hasEmail))throw new InvalidOperationException("Enter a valid email address or leave it blank to generate a link.");
  if(emailRequested==true&&(!hasEmail||!CanEmail))throw new InvalidOperationException("Email delivery needs a valid address, a public HTTPS ATS address, and working email settings.");
  var sendEmail=emailRequested??(hasEmail&&CanEmail);
  var key="client:"+clientId.ToString("N");
  var grant=await db.HiringReviewGrants.Where(g=>g.ClientId==clientId&&!g.Revoked).OrderBy(g=>g.CreatedUtc).FirstOrDefaultAsync();
  grant??=await db.HiringReviewGrants.SingleOrDefaultAsync(g=>g.EmailKey==key);
  string token;

  if(grant is null){token=WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));grant=new(){ClientId=clientId,EmailKey=key,Name=client.Name,TokenHash=Hash(token),ProtectedToken=tokenProtector.Protect(token)};db.HiringReviewGrants.Add(grant);}

  else {if(grant.Revoked)throw new InvalidOperationException("The Client Review Link Was Revoked. Ask The Workspace Owner To Generate A Replacement Before Sharing More Profiles.");token=tokenProtector.Unprotect(grant.ProtectedToken);}

  if(grant.Revoked||grant.ExpiresUtc<=DateTime.UtcNow){

   if(grant.CreatedUtc<DateTime.UtcNow.AddSeconds(-1)){token=WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));grant.TokenHash=Hash(token);grant.ProtectedToken=tokenProtector.Protect(token);}

  }

  grant.ClientId=clientId;grant.Name=client.Name;grant.Revoked=false;grant.ExpiresUtc=DateTime.SpecifyKind(DateTime.MaxValue,DateTimeKind.Utc);

  var submitted=new List<HiringSubmission>();

  foreach(var a in apps){

   // Check access again immediately before the write, including reassignment.

   await access.RequireApplication(a.Id);

   var sub=await db.HiringSubmissions.SingleOrDefaultAsync(s=>s.GrantId==grant.Id&&s.AssessmentId==a.Id);

   if(sub is null){sub=new(){GrantId=grant.Id,AssessmentId=a.Id};db.HiringSubmissions.Add(sub);}

   sub.ContactId=contact?.Id??Guid.Empty;sub.SubmittedBy=uid;sub.SharedReportJson=JsonSerializer.Serialize(Shared(a));sub.SubmittedUtc=DateTime.UtcNow;sub.Revoked=false;sub.Delivery=sendEmail?"Email Pending":hasEmail&&emailRequested is null?"Email Not Sent":"Review Link Created";

   if(sub.Feedback=="More Information Required"){
    sub.Feedback="Awaiting Feedback";var action=await db.RecruiterActions.SingleOrDefaultAsync(t=>t.AssessmentId==a.Id&&t.Status=="Open");if(action is not null){action.Status="Completed";action.CompletedUtc=DateTime.UtcNow;}
   }
   if(sub.Feedback=="Awaiting Feedback"){a.ClientStatus="Submitted";a.Stage="Awaiting Client Feedback";}

   AtsService.Audit(db,uid,"Profile Forwarded",sub.Id.ToString(),$"Submitted for {client.Name} feedback",a.CandidateId,a.Id);submitted.Add(sub);

  }

  await db.SaveChangesAsync();updates.Publish();

  var link=BaseUrl+"/review/access?token="+Uri.EscapeDataString(token);

  if(!sendEmail&&hasEmail&&emailRequested is null)return new(link,CanShareExternally?"Email Not Sent: email delivery is not configured. The review link was generated for copying.":"Email Not Sent: configure a public HTTPS ATS address first. The review link was generated for testing on this computer only.");
  if(!sendEmail)return new(link,CanShareExternally?"Review link generated. Copy it and paste it into WhatsApp or your preferred channel.":"Review link generated for testing on this computer. A public HTTPS ATS address is needed for clients to open it on their phones.");

  try{

   await email.Send(address,"Osynix — Profiles Ready For Review",$"Hello {client.Name} Team,\n\n{apps.Count} profiles are ready for {apps[0].Position!.Title} (#{apps[0].Position!.Reference}).\n\n{message}\n\nReview assessment reports, add feedback, and propose interview availability:\n{link}\n\nUse the same link to revisit profiles and positions shared for your client team. You may share it with authorized team members reviewing on your behalf. The same link remains available until the workspace owner replaces or revokes it. Candidate contact details and CVs are not shared.");

   foreach(var sub in submitted)sub.Delivery="Email Sent";await db.SaveChangesAsync();updates.Publish();

   return new(link,"Profiles forwarded and review email sent.");

  }catch{return new(link,"Profiles forwarded, but email delivery could not be confirmed. Keep the review link and retry sending.");}

 }

 public async Task<(string Cookie,DateTime Expires)?> Exchange(string token){

  if(token.Length is <30 or >200)return null;

  await using var db=await factory.CreateDbContextAsync();var hash=Hash(token);

  var grant=await db.HiringReviewGrants.AsNoTracking().SingleOrDefaultAsync(g=>g.TokenHash==hash&&!g.Revoked&&g.ExpiresUtc>DateTime.UtcNow);

  return grant is null?null:(cookieProtector.Protect(grant.Id+":"+grant.TokenHash),grant.ExpiresUtc);

 }

 async Task<HiringReviewGrant> Grant(AtsDbContext db,string? cookie){

  Guid id;string hash;try{var values=cookieProtector.Unprotect(cookie??"").Split(':');id=Guid.Parse(values[0]);hash=values[1];}catch{throw new UnauthorizedAccessException("This review link is invalid or expired.");}

  return await db.HiringReviewGrants.SingleOrDefaultAsync(g=>g.Id==id&&g.TokenHash==hash&&!g.Revoked&&g.ExpiresUtc>DateTime.UtcNow)??throw new UnauthorizedAccessException("This review link is invalid or expired.");

 }

 IQueryable<HiringSubmission> Visible(AtsDbContext db,HiringReviewGrant grant){
  if(grant.ClientId is Guid clientId)
   return db.HiringSubmissions.Where(s=>!s.Revoked&&db.HiringReviewGrants.Any(g=>g.Id==s.GrantId&&g.ClientId==clientId&&!g.Revoked)&&db.Assessments.Any(a=>a.Id==s.AssessmentId&&a.State=="Saved"&&a.Stage!="Rejected"&&a.Position!.ClientId==clientId));
  var contactBound=grant.EmailKey.StartsWith("contact:",StringComparison.Ordinal);
  var contactId=contactBound&&Guid.TryParse(grant.EmailKey["contact:".Length..],out var parsed)?parsed:Guid.Empty;
  return db.HiringSubmissions.Where(s=>s.GrantId==grant.Id&&!s.Revoked&&db.Assessments.Any(a=>a.Id==s.AssessmentId&&a.State=="Saved"&&a.Stage!="Rejected"&&db.ClientContacts.Any(c=>c.Id==s.ContactId&&c.ClientId==a.Position!.ClientId&&((contactBound&&c.Id==contactId)||(!contactBound&&c.Email.Trim().ToLower()==grant.EmailKey)))));
 }
 public async Task<(string Name,List<HiringRow> Rows)> Read(string? cookie){

  await using var db=await factory.CreateDbContextAsync();var grant=await Grant(db,cookie);

  var rows=await (from s in Visible(db,grant) join a in db.Assessments on s.AssessmentId equals a.Id join p in db.Positions on a.PositionId equals p.Id orderby s.SubmittedUtc descending,s.Id select new{s,a,p}).AsNoTracking().ToListAsync();

  var interviews=await db.InterviewBookings.AsNoTracking().Where(i=>rows.Select(x=>x.a.Id).Contains(i.AssessmentId)).ToListAsync();
  return (grant.Name,rows.Select(x=>{var report=JsonSerializer.Deserialize<Assessment>(x.s.SharedReportJson)!;var snap=JsonSerializer.Deserialize<AssessmentSnapshot>(report.SnapshotJson)!;return new HiringRow(x.s.Id,x.p.Id,x.p.Reference,x.p.Title,snap.Profile.Name,report.MatchPercent,report.Decision,x.a.Stage,x.a.InterviewStatus,x.a.ClientStatus,x.s.Feedback,x.s.Remarks,x.s.SubmittedUtc,x.s.FeedbackUtc,x.s.AvailabilityStartUtc,x.s.DurationMinutes,x.s.TimeZone,x.s.InterviewMode,x.s.InterviewLocation,x.s.Revision,x.a.Revision,Lifecycle.Classify(x.a),report.MustHaveFit,x.s.RecruiterResponse,x.s.RecruiterResponseUtc,interviews.Where(i=>i.AssessmentId==x.a.Id).OrderByDescending(i=>i.CreatedUtc).Select(InterviewService.Summary).FirstOrDefault());}).ToList());

 }

 public async Task<List<Assessment>> Reports(string? cookie,Guid? submissionId,Guid? positionId){

  await using var db=await factory.CreateDbContextAsync();var grant=await Grant(db,cookie);

  var query=Visible(db,grant);

  if(submissionId is Guid id)query=query.Where(s=>s.Id==id);

  else if(positionId is Guid pid)query=query.Where(s=>db.Assessments.Any(a=>a.Id==s.AssessmentId&&a.PositionId==pid));

  else throw new InvalidOperationException("Choose a profile or position.");

  var rows=await query.OrderBy(s=>s.SubmittedUtc).Take(50).AsNoTracking().ToListAsync();if(rows.Count==0)throw new UnauthorizedAccessException("This report has not been shared with you.");

  return rows.Select(s=>JsonSerializer.Deserialize<Assessment>(s.SharedReportJson)!).ToList();

 }

 public async Task Reply(string? cookie,HiringFeedback input){

  if(!FeedbackChoices.Contains(input.Status)||input.Remarks is null||input.Remarks.Length>5000)throw new InvalidOperationException("Choose a feedback status and keep remarks within 5,000 characters.");

  if(input.Status=="Request Interview"&&string.IsNullOrWhiteSpace(input.LocalStart))throw new InvalidOperationException("Provide interview availability when requesting an interview.");

  DateTime? start=null;

  if(input.Status=="Request Interview"&&!string.IsNullOrWhiteSpace(input.LocalStart)){

   if(input.Mode is not ("Online" or "On-Site")||input.DurationMinutes is <15 or >240||input.TimeZone is null||input.Location is null||input.TimeZone.Length>100||input.Location.Length>500||(input.Mode=="On-Site"&&string.IsNullOrWhiteSpace(input.Location)))throw new InvalidOperationException("Choose interview mode, duration, time zone, and the on-site location.");

   TimeZoneInfo zone;try{zone=TimeZoneInfo.FindSystemTimeZoneById(input.TimeZone);}catch{throw new InvalidOperationException("Choose a valid time zone.");}

   if(!DateTime.TryParseExact(input.LocalStart,["yyyy-MM-ddTHH:mm","yyyy-MM-ddTHH:mm:ss"],CultureInfo.InvariantCulture,DateTimeStyles.None,out var local)||zone.IsInvalidTime(local)||zone.IsAmbiguousTime(local))throw new InvalidOperationException("Choose an unambiguous local date and time.");

   start=TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local,DateTimeKind.Unspecified),zone);

   if(start<=DateTime.UtcNow||start>DateTime.UtcNow.AddYears(1))throw new InvalidOperationException("Choose a future interview time within one year.");

  }

  await using var db=await factory.CreateDbContextAsync();var grant=await Grant(db,cookie);

  var sub=await Visible(db,grant).SingleOrDefaultAsync(s=>s.Id==input.SubmissionId)??throw new UnauthorizedAccessException("This profile has not been shared with you.");

  var a=await db.Assessments.SingleAsync(a=>a.Id==sub.AssessmentId);

  if(sub.Revision!=input.Revision||a.Revision!=input.ApplicationRevision)throw new DbUpdateConcurrencyException("The profile changed. Reload it before submitting feedback.");

  if(a.Stage=="Hired"||a.ClientStatus=="Hired"||a.Stage=="Offer Sent")throw new InvalidOperationException("This application has progressed beyond review. Contact your Recruiter.");

  var position=await db.Positions.SingleAsync(p=>p.Id==a.PositionId);if(position.Status is not("Active" or "On Hold"))throw new InvalidOperationException("This Position Is Closed. Contact Your Recruiter Before Sending More Feedback.");
  var normalizedZone=start is null?"":input.TimeZone;var normalizedMode=start is null?"":input.Mode;var normalizedLocation=start is null?"":input.Location.Trim();
  var booking=await db.InterviewBookings.Where(i=>i.AssessmentId==a.Id).OrderByDescending(i=>i.CreatedUtc).FirstOrDefaultAsync();
  if(input.InterviewRevision is Guid interviewRevision&&(booking is null||booking.Revision!=interviewRevision))throw new DbUpdateConcurrencyException("The Candidate Response Or Interview Changed. Reopen The Profile Before Evaluating.");
  if(input.InterviewCompleted){
   if(booking is null||input.InterviewRevision is null||booking.Status is not("Confirmed" or "Completed"))throw new InvalidOperationException("Only A Confirmed Interview Can Be Evaluated As Completed.");
   if(booking.Status!="Completed"){
    booking.Status="Completed";a.InterviewStatus="Completed";
    AtsService.Audit(db,"HiringManager","Interview completion confirmed",booking.Id.ToString(),"Interviewer Confirmed The Interview Took Place",a.CandidateId,a.Id);db.Audit.Local.Last().ActorName="Submitted Through Client Review Link";
   }
  }
  if(booking is not null&&(booking.Status is "Awaiting Confirmation" or "Confirmed" or "New Time Proposed")&&input.Status is not("Reject" or "Hold"))throw new InvalidOperationException("Confirm That The Interview Took Place Before Choosing Its Next Step. Pending Time Proposals Must Be Resolved First.");
  if(input.Status is "Recommend Hire" or "Send Offer"&&booking?.Status!="Completed")throw new InvalidOperationException("Complete The Interview Before Requesting An Offer.");
  if(input.Status=="Send Offer"&&input.InterviewRevision is null)throw new InvalidOperationException("Reopen The Interview Evaluation Before Requesting An Offer.");
  if(booking is not null&&(booking.Status is not("Completed" or "Cancelled"))&&(input.Status is "Reject" or "Hold")){
   await new InterviewService(factory,access,protection,config,email,updates).CancelForFeedback(db,booking,a);
  }
  if(sub.Feedback==input.Status&&sub.Remarks==input.Remarks.Trim()&&sub.AvailabilityStartUtc==start&&sub.TimeZone==normalizedZone&&sub.InterviewMode==normalizedMode&&sub.InterviewLocation==normalizedLocation&&sub.DurationMinutes==input.DurationMinutes&&!input.InterviewCompleted)return;
  var previous=JsonSerializer.Serialize(new{a.Stage,a.ClientStatus,sub.Feedback,sub.Remarks});

  var client=input.Status switch{"Shortlist"=>"Client Shortlisted","Request Interview"=>"Interview Requested","More Information Required"=>"Information Requested","Send Offer"=>"Offer Requested","Recommend Hire"=>"Client Shortlisted","Reject"=>"Client Rejected",_=>"Under Review"};

  var stage=input.Status switch{"Shortlist"=>"Shortlisted","Request Interview"=>"Interview Requested","More Information Required"=>"Information Requested","Send Offer"=>"Offer Requested","Recommend Hire"=>"Shortlisted","Reject"=>"Rejected",_=>"Hold"};

  var interview=input.Status=="Request Interview"?(a.InterviewStatus=="Scheduled"?"Reschedule Required":"Not Scheduled"):input.Status=="Reject"?"Not Required":a.InterviewStatus;
  var state=Lifecycle.Synchronize(stage,interview,client,a.ClientStatus);

  a.Stage=state.Stage;a.ClientStatus=state.Client;a.InterviewStatus=state.Interview;

  sub.Feedback=input.Status;sub.Remarks=input.Remarks.Trim();sub.FeedbackUtc=DateTime.UtcNow;

  sub.AvailabilityStartUtc=start;sub.DurationMinutes=input.DurationMinutes;sub.TimeZone=start is null?"":input.TimeZone;sub.InterviewMode=start is null?"":input.Mode;sub.InterviewLocation=start is null?"":input.Location.Trim();

  AtsService.Audit(db,"HiringManager","Hiring Manager Feedback",sub.Id.ToString(),$"{input.Status}: {sub.Remarks}"+(start is null?"":$"\nInterview Availability: {start:yyyy-MM-dd HH:mm} UTC · {input.Mode} · {input.DurationMinutes} Minutes"),a.CandidateId,a.Id,previous,JsonSerializer.Serialize(new{a.Stage,a.ClientStatus,sub.Feedback,sub.Remarks,sub.AvailabilityStartUtc}));

  db.Audit.Local.Last().ActorName="Submitted Through Client Review Link";await RecruiterActionService.Capture(db,sub,a);await db.SaveChangesAsync();updates.Publish();if(booking?.Status=="Cancelled")await new InterviewService(factory,access,protection,config,email,updates).FlushQueued(booking.Id);

 }

 public async Task<List<RecruiterFeedback>> Inbox(){

  var ids=(await new AtsService(factory,access).Assessments()).Select(a=>a.Id).ToList();

  await using var db=await factory.CreateDbContextAsync();

  return await (from s in db.HiringSubmissions.AsNoTracking() join g in db.HiringReviewGrants on s.GrantId equals g.Id where ids.Contains(s.AssessmentId)&&!s.Revoked&&!g.Revoked orderby s.SubmittedUtc descending select new RecruiterFeedback(s.Id,s.AssessmentId,g.Name,s.Feedback,s.Remarks,s.AvailabilityStartUtc,s.DurationMinutes,s.TimeZone,s.InterviewMode,s.InterviewLocation,s.Delivery)).ToListAsync();

 }

 public async Task<Guid> SharedApplication(string? cookie,Guid submissionId){await using var db=await factory.CreateDbContextAsync();var grant=await Grant(db,cookie);return (await Visible(db,grant).SingleOrDefaultAsync(s=>s.Id==submissionId)??throw new UnauthorizedAccessException()).AssessmentId;}
 public async Task Revoke(Guid submissionId){

  await using var db=await factory.CreateDbContextAsync();var sub=await db.HiringSubmissions.SingleAsync(s=>s.Id==submissionId);await access.RequireApplication(sub.AssessmentId);sub.Revoked=true;var action=await db.RecruiterActions.SingleOrDefaultAsync(t=>t.AssessmentId==sub.AssessmentId&&t.Status=="Open");if(action is not null){action.Status="Cancelled";action.CompletedUtc=DateTime.UtcNow;}var a=await db.Assessments.SingleAsync(a=>a.Id==sub.AssessmentId);AtsService.Audit(db,await access.UserId(),"Profile Sharing Revoked",sub.Id.ToString(),"Hiring Manager access removed",a.CandidateId,a.Id);await db.SaveChangesAsync();updates.Publish();

 }


 public async Task<ClientReviewLink> ClientLink(Guid clientId,Guid? positionId=null,Guid? contactId=null){await access.RequireClient(clientId);var active=await access.Context();if(active?.Owner!=true&&!(await new AtsService(factory,access).Positions()).Any(p=>p.ClientId==clientId))throw new UnauthorizedAccessException("This Client Is Not Assigned To You.");await using var db=await factory.CreateDbContextAsync();if(contactId is Guid cid&&!await db.ClientContacts.AnyAsync(c=>c.Id==cid&&c.ClientId==clientId))throw new UnauthorizedAccessException();if(positionId is Guid pid){await access.RequirePosition(pid);if(!await db.Positions.AnyAsync(p=>p.Id==pid&&p.ClientId==clientId))throw new UnauthorizedAccessException();}var grant=await db.HiringReviewGrants.AsNoTracking().Where(g=>g.ClientId==clientId).OrderBy(g=>g.Revoked).ThenBy(g=>g.CreatedUtc).FirstOrDefaultAsync();if(grant is null)return new(null,"Not Created",null,0);var count=await db.HiringSubmissions.CountAsync(s=>s.GrantId==grant.Id&&!s.Revoked);var status=grant.Revoked?"Revoked":grant.ExpiresUtc<=DateTime.UtcNow?"Expired":"Active";var link=status=="Active"?BaseUrl+"/review/access?token="+Uri.EscapeDataString(tokenProtector.Unprotect(grant.ProtectedToken))+(positionId.HasValue?"&position="+positionId.Value:""):null;return new(link,status,grant.ExpiresUtc,count);}
 public async Task ManageClientLink(Guid clientId,bool replace){
  await access.RequireClient(clientId);var active=await access.Context();if(active?.Owner!=true&&!(await access.Require()).IsInRole("Admin"))throw new UnauthorizedAccessException("Only The Workspace Owner Can Manage The Client Review Link.");
  await using var db=await factory.CreateDbContextAsync();var client=await db.Clients.SingleOrDefaultAsync(c=>c.Id==clientId)??throw new InvalidOperationException("Client Not Found.");var grants=await db.HiringReviewGrants.Where(g=>g.ClientId==clientId).OrderBy(g=>g.Revoked).ThenBy(g=>g.CreatedUtc).ToListAsync();var grant=grants.FirstOrDefault();
  if(replace){
   var token=WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
   if(grant is null){grant=new(){ClientId=clientId,EmailKey="client:"+clientId.ToString("N"),Name=client.Name};db.HiringReviewGrants.Add(grant);}
   grant.TokenHash=Hash(token);grant.ProtectedToken=tokenProtector.Protect(token);grant.ExpiresUtc=DateTime.SpecifyKind(DateTime.MaxValue,DateTimeKind.Utc);grant.Revoked=false;grant.Name=client.Name;
  }else{
   if(grants.Count==0)throw new InvalidOperationException("There Is No Hiring Manager Link To Revoke.");
   foreach(var g in grants)g.Revoked=true;var appIds=await db.HiringSubmissions.Where(s=>grants.Select(g=>g.Id).Contains(s.GrantId)).Select(s=>s.AssessmentId).ToListAsync();foreach(var task in await db.RecruiterActions.Where(t=>appIds.Contains(t.AssessmentId)&&t.Status=="Open").ToListAsync()){task.Status="Cancelled";task.CompletedUtc=DateTime.UtcNow;}
  }
  AtsService.Audit(db,await access.UserId(),replace?"Client review link created or replaced":"Client review link revoked",clientId.ToString(),"Shared Assessment And Feedback History Preserved");await db.SaveChangesAsync();updates.Publish();
 }
 public async Task RespondToInformation(Guid actionId,Guid revision,string response){if(string.IsNullOrWhiteSpace(response)||response.Length>5000)throw new InvalidOperationException("Enter A Client Response Up To 5,000 Characters.");await using var db=await factory.CreateDbContextAsync();await using var tx=await db.Database.BeginTransactionAsync();var action=await db.RecruiterActions.SingleOrDefaultAsync(t=>t.Id==actionId&&t.Status=="Open")??throw new InvalidOperationException("This Task Is No Longer Open.");await access.RequireApplication(action.AssessmentId);if(action.Revision!=revision)throw new DbUpdateConcurrencyException("Feedback Changed. Reload Before Responding.");var uid=await access.UserId();if(action.RecipientId!=uid&&(await access.Context())?.Owner!=true&&!(await access.Require()).IsInRole("Admin"))throw new UnauthorizedAccessException();var sub=await db.HiringSubmissions.SingleAsync(s=>s.Id==action.SubmissionId);var grant=await db.HiringReviewGrants.SingleAsync(g=>g.Id==sub.GrantId);var app=await db.Assessments.SingleAsync(a=>a.Id==action.AssessmentId);if(action.Type!="Respond To Client"||sub.Revoked||grant.Revoked||grant.ExpiresUtc<=DateTime.UtcNow||app.Stage is "Hired" or "Rejected" or "Offer Sent")throw new InvalidOperationException("This Application Cannot Receive An Information Response.");sub.RecruiterResponse=ReportService.ClientSafeText(response.Trim());sub.RecruiterResponseUtc=DateTime.UtcNow;sub.Feedback="Awaiting Feedback";app.Stage="Awaiting Client Feedback";app.ClientStatus="Submitted";action.Status="Completed";action.CompletedUtc=DateTime.UtcNow;AtsService.Audit(db,uid,"Information sent for client review",app.Id.ToString(),"Client-Safe Response Published",app.CandidateId,app.Id);await db.SaveChangesAsync();await tx.CommitAsync();updates.Publish();}
}
