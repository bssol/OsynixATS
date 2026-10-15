using System.ComponentModel.DataAnnotations;
namespace Osynix.Ats.Domain;
public class HiringReviewGrant : Record {
 public Guid? ClientId{get;set;}
 [MaxLength(320)]public string EmailKey{get;set;}="";
 public string Name{get;set;}="";
 public string TokenHash{get;set;}="";
 public string ProtectedToken{get;set;}="";
 public DateTime ExpiresUtc{get;set;}
 public bool Revoked{get;set;}
}
public class HiringSubmission : Record {
 public Guid GrantId{get;set;}
 public Guid AssessmentId{get;set;}
 public Guid ContactId{get;set;}
 public string SubmittedBy{get;set;}="";
 public string SharedReportJson{get;set;}="";
 public string Feedback{get;set;}="Awaiting Feedback";
 public string Remarks{get;set;}="";
 public DateTime SubmittedUtc{get;set;}=DateTime.UtcNow;
 public DateTime? FeedbackUtc{get;set;}
 public DateTime? AvailabilityStartUtc{get;set;}
 public int DurationMinutes{get;set;}=30;
 public string TimeZone{get;set;}="";
 public string InterviewMode{get;set;}="";
 public string InterviewLocation{get;set;}="";
 public string Delivery{get;set;}="Review Link Created";
 public bool Revoked{get;set;}
}
