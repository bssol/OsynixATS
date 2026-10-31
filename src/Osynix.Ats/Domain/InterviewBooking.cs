using System.ComponentModel.DataAnnotations;
namespace Osynix.Ats.Domain;
public class InterviewBooking : Record {
 public Guid AssessmentId{get;set;}
 public Guid SubmissionId{get;set;}
 public string OrganizerId{get;set;}="";
 public string CandidateEmail{get;set;}="";
 public string ManagerEmail{get;set;}="";
 public string Mode{get;set;}="Online";
 public string Provider{get;set;}="Manual Link";
 public string MeetingLink{get;set;}="";
 public string Location{get;set;}="";
 public string Message{get;set;}="";
 public DateTime StartUtc{get;set;}
 public int DurationMinutes{get;set;}=30;
 public string TimeZone{get;set;}="UTC";
 public string Status{get;set;}="Awaiting Confirmation";
 public string TokenHash{get;set;}="";
 public string ProtectedToken{get;set;}="";
 public DateTime ExpiresUtc{get;set;}
 public DateTime? ProposedStartUtc{get;set;}
 public string ProposedTimeZone{get;set;}="";
 public string ResponseNotes{get;set;}="";
 public string ResponseSource{get;set;}="";
 public DateTime? RespondedUtc{get;set;}
}
public class InterviewDelivery : Record {
 public Guid InterviewId{get;set;}
 public string Recipient{get;set;}="";
 public string Subject{get;set;}="";
 public string Body{get;set;}="";
 public string Status{get;set;}="Pending";
 public DateTime? SentUtc{get;set;}
}
public record InterviewSummary(Guid Id,Guid Revision,Guid AssessmentId,string Status,DateTime StartUtc,int DurationMinutes,string TimeZone,string Mode,string Provider,string MeetingLink,string Location,DateTime? ProposedStartUtc,string ProposedTimeZone,string ResponseNotes,string ResponseSource);
public static class PipelineWorkflow {
 public static bool IsFinal(Assessment a)=>a.Stage is "Rejected" or "Hired"||a.ClientStatus is "Client Rejected" or "Hired";
 public static string CurrentStatus(Assessment a,bool submitted,InterviewSummary? i,bool clientShortlisted=false)=>a.Stage=="Rejected"||a.ClientStatus=="Client Rejected"?"Rejected":a.Stage=="Hired"||a.ClientStatus=="Hired"?"Hired":a.Stage=="Offer Sent"||a.ClientStatus=="Offer"?"Offer Sent":a.Stage=="Offer Requested"?"Offer Requested":clientShortlisted?"Awaiting Decision":i is not null&&i.Status is not("Cancelled" or "Completed")?i.Status:a.InterviewStatus is "Scheduled" or "Cancelled" or "New Time Proposed"?a.InterviewStatus:a.Stage=="Information Requested"?"Information Requested":a.Stage=="Interview Requested"?"Interview Requested":a.Stage=="Hold"?"On Hold":a.Stage=="Awaiting Client Feedback"?"Awaiting Decision":a.Stage=="Client Interview"&&a.InterviewStatus=="Completed"?"Awaiting Interview Feedback":a.Stage=="Awaiting Employer Review"?"Awaiting Employer Review":submitted?"In Review":"Not Submitted";
 public static bool MatchesCard(Assessment a,InterviewSummary? i,string key,bool submitted,bool hasAction=false,bool clientShortlisted=false){
  if(key.Length>0&&key is not("submitted" or "offer-requested" or "offer-sent" or "hired" or "rejected")&&a.Position?.Status is not("Active" or "On Hold"))return false;
  return key switch{
   "submitted"=>submitted,
   "offer-requested"=>!IsFinal(a)&&a.Stage=="Offer Requested",
   "offer-sent"=>!IsFinal(a)&&(a.Stage=="Offer Sent"||a.ClientStatus=="Offer"),
   "hired"=>CurrentStatus(a,submitted,i)=="Hired",
   "rejected"=>CurrentStatus(a,submitted,i)=="Rejected",
   "action"=>!clientShortlisted&&!IsFinal(a)&&a.Stage!="Offer Sent"&&(ActionNeeded(a,i)||hasAction),
   "decision"=>!IsFinal(a)&&(a.Stage=="Awaiting Client Feedback"||clientShortlisted),
   "hold"=>!IsFinal(a)&&a.Stage=="Hold",
   "confirmation"=>!IsFinal(a)&&a.Stage is not("Offer Sent" or "Offer Requested")&&(i?.Status=="Awaiting Confirmation"||i is null&&a.InterviewStatus=="Scheduled"&&a.InterviewDateTimeUtc>DateTime.UtcNow),
   "upcoming"=>!IsFinal(a)&&a.Stage is not("Offer Sent" or "Offer Requested")&&i?.Status=="Confirmed"&&i.StartUtc>DateTime.UtcNow,
   _=>true
  };
 }
 public static bool ActionNeeded(Assessment a,InterviewSummary? i)=>a.Stage is not("Rejected" or "Hired" or "Offer Sent")&&(i?.Status is "Declined" or "New Time Proposed" or "Response Requires Review"||i is null&&a.InterviewStatus=="New Time Proposed"||i?.Status=="Confirmed"&&i.StartUtc.AddMinutes(i.DurationMinutes)<=DateTime.UtcNow||a.Stage is "Information Requested" or "Interview Requested" or "Offer Requested");
}
