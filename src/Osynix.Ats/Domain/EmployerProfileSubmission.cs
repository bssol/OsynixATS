namespace Osynix.Ats.Domain;
public class EmployerProfileSubmission:Record {
 public Guid WorkspaceId{get;set;}public Guid AssessmentId{get;set;}public string SubmittedBy{get;set;}="";public DateTime SubmittedUtc{get;set;}=DateTime.UtcNow;public int AssessmentVersion{get;set;}=1;
 public string Status{get;set;}="Awaiting Employer Review";public string Remarks{get;set;}="";public DateTime? FeedbackUtc{get;set;}
}
