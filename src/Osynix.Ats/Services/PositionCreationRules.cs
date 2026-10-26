using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Osynix.Ats.Domain;
namespace Osynix.Ats.Services;
public static class PositionCreationRules {
 public static readonly string[] EmploymentTypes=["Full-time","Part-time","Contract","Temporary","Internship","Apprenticeship","Freelance","Other"];
 public static string SourceSignature(Position p)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new{p.ClientId,p.Client,p.Title,p.Industry,p.Seniority,p.JobDescription,p.JobDescriptionHtml,p.ClientNotes}))));
 public static string LegacySourceSignature(Position p)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new{p.ClientId,p.Client,p.JobDescription,p.ClientNotes}))));
 public static string CriteriaSignature(Position p)=>JsonSerializer.Serialize(new{p.PositionSummary,Criteria=p.Criteria.Select(c=>new{c.Name,c.Weight,c.Knockout,c.EvidenceExpected,c.ScoringGuidance,c.Notes,c.Rationale})});
 public static List<string> RequiredDetails(Position p,bool companyForm){var missing=new List<string>();if(!companyForm&&p.ClientId is null)missing.Add("Client");if(string.IsNullOrWhiteSpace(p.Title))missing.Add("Position Title");if(p.HiringTarget<1)missing.Add("Hiring Target (At Least 1)");if(!Choices.Priorities.Contains(p.Priority))missing.Add("Priority");if(string.IsNullOrWhiteSpace(p.EmploymentType))missing.Add("Employment Type");return missing;}
 public static bool CanOpenStep(string step,Position p,bool companyForm,bool analyzed,bool sourceVerified,bool criteriaApproved){var ready=RequiredDetails(p,companyForm).Count==0;return step switch{"basics"=>true,"jd"=>analyzed&&ready,"criteria"=>analyzed&&ready&&sourceVerified,"status"=>analyzed&&ready&&sourceVerified&&criteriaApproved&&ValidCriteria(p),_=>false};}
 public static bool ValidCriteria(Position p){try{ScoringEngine.ValidateCriteria(p.Criteria);return true;}catch(InvalidOperationException){return false;}}
 public static bool ApplyReviewedJd(Position p,string html){
  var wasCurrent=p.DraftAnalysisSignature==SourceSignature(p);
  var previousText=p.JobDescription;
  p.JobDescriptionHtml=JdTemplateFormatter.SanitizeHtml(html);
  p.JobDescription=JdTemplateFormatter.ToPlainText(p.JobDescriptionHtml);
  if(string.IsNullOrWhiteSpace(p.PublicJobDescription)||string.Equals(p.PublicJobDescription,previousText,StringComparison.Ordinal))p.PublicJobDescription=p.JobDescription;
  if(wasCurrent)p.DraftAnalysisSignature=SourceSignature(p);
  return !string.Equals(previousText,p.JobDescription,StringComparison.Ordinal);
 }
 public static void Apply(Position p,JdAnalysis analysis){
  var previousJobDescription=p.JobDescription;
  if(!string.IsNullOrWhiteSpace(analysis.Title))p.Title=analysis.Title.Trim();
  if(!string.IsNullOrWhiteSpace(analysis.Location))p.Location=analysis.Location.Trim();
  if(!string.IsNullOrWhiteSpace(analysis.Department))p.Department=analysis.Department.Trim();
  var employment=EmploymentTypes.FirstOrDefault(t=>t.Equals(analysis.EmploymentType,StringComparison.OrdinalIgnoreCase));if(employment is not null)p.EmploymentType=employment;
  if(new[]{"On-Site","Hybrid","Remote"}.Contains(analysis.WorkArrangement))p.WorkArrangement=analysis.WorkArrangement;
  if(analysis.HiringTarget is >0)p.HiringTarget=analysis.HiringTarget.Value;
  if(!string.IsNullOrWhiteSpace(analysis.SalaryRange))p.SalaryRange=analysis.SalaryRange.Trim();
  if(analysis.SalaryMinimum is >=0)p.SalaryMinimum=analysis.SalaryMinimum;
  if(analysis.SalaryMaximum is >=0)p.SalaryMaximum=analysis.SalaryMaximum;
  if(analysis.SalaryCurrency.Length==3&&analysis.SalaryCurrency.All(char.IsAsciiLetter))p.SalaryCurrency=analysis.SalaryCurrency.ToUpperInvariant();
  if(new[]{"Hourly","Daily","Weekly","Monthly","Annually"}.Contains(analysis.SalaryPeriod))p.SalaryPeriod=analysis.SalaryPeriod;
  p.PositionSummary=!string.IsNullOrWhiteSpace(analysis.Summary)?analysis.Summary:analysis.Sections?.FirstOrDefault(x=>x.Key=="summary")?.Content??"";p.Criteria=analysis.Criteria;
  if(!string.IsNullOrWhiteSpace(analysis.Industry))p.Industry=analysis.Industry.Trim();
  if(!string.IsNullOrWhiteSpace(analysis.Seniority))p.Seniority=analysis.Seniority.Trim();
  if(analysis.Sections is {Count:>0}){
   var sections=JdTemplateFormatter.Complete(analysis.Sections,analysis.AttentionSections);
   p.JobDescriptionHtml=JdTemplateFormatter.ToHtml(sections);
   p.JobDescription=JdTemplateFormatter.ToPlainText(p.JobDescriptionHtml);
   p.JdNeedsAttentionJson=JsonSerializer.Serialize(sections.Where(x=>x.NeedsAttention).Select(x=>x.Key));
   if(string.IsNullOrWhiteSpace(p.PublicJobDescription)||string.Equals(p.PublicJobDescription,previousJobDescription,StringComparison.Ordinal))p.PublicJobDescription=p.JobDescription;
  }
 }
}
