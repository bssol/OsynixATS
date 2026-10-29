namespace Osynix.Ats.Domain;
public static class AccountTypes {public const string Freelance="Freelance Recruiter",Employer="Employer",Employee="Team Member",Legacy="Legacy";public static string Label(string role)=>role=="Recruiter"?Freelance:role;}
public class WorkspaceMembership:Record {
 public Guid WorkspaceId{get;set;} public string UserId{get;set;}="";public string Status{get;set;}="Active";
 public bool CanCreateClients{get;set;} public bool CanCreatePositions{get;set;} public bool CanExportReports{get;set;} public bool CanViewTalentPool{get;set;}=true;public bool CanUseMasterPool{get;set;}=true;
}
public class TalentPerson:Record {public string NameKey{get;set;}="";public string EmailKey{get;set;}="";public string PhoneKey{get;set;}="";public Guid? CanonicalCandidateId{get;set;}public bool NeedsReview{get;set;}}
