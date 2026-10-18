namespace Osynix.Ats.Domain;
public class FreelancerProfile:Record {
 public string TimeZone{get;set;}="Asia/Karachi";
 public string Languages{get;set;}="";
 public string HiringMarkets{get;set;}="";
 public string WeeklyAvailability{get;set;}="";
 public string Industries{get;set;}="";
 public string Technologies{get;set;}="";
 public string SeniorityLevels{get;set;}="";
 public string WorkingStyle{get;set;}="";
 public string Experience{get;set;}="";
 public string Education{get;set;}="";
 public string CaseStudies{get;set;}="";
 public string UserId{get;set;}="";public bool Published{get;set;}public string Headline{get;set;}="";public string Skills{get;set;}="";public string Bio{get;set;}="";public string Location{get;set;}="";public int YearsExperience{get;set;}public string Availability{get;set;}="Available";
}
public class PositionRecruiterAssignment:Record {public Guid PositionId{get;set;}public string UserId{get;set;}="";}
