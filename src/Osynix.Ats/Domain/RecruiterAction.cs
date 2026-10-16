namespace Osynix.Ats.Domain;
public class RecruiterAction:Record {
 public Guid AssessmentId{get;set;}public Guid SubmissionId{get;set;}public string? RecipientId{get;set;}public string Type{get;set;}="Review Feedback";public string Title{get;set;}="";public string Feedback{get;set;}="";public string Remarks{get;set;}="";public string Status{get;set;}="Open";public DateTime FeedbackUtc{get;set;}=DateTime.UtcNow;public DateTime? CompletedUtc{get;set;}
}
