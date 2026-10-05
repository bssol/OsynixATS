using System.Globalization;
using System.Text.Json;

namespace Osynix.Ats.Domain;

public static class CandidateProfile
{
    static string Text(JsonElement item, params string[] names) => names.Select(n=>item.TryGetProperty(n,out var p) && p.ValueKind==JsonValueKind.String ? p.GetString() : null).FirstOrDefault(x=>!string.IsNullOrWhiteSpace(x)) ?? "";

    public static void ReadStructuredHistory(Candidate person)
    {
        if (person.EmploymentHistory.Count==0) person.EmploymentHistory=Parse(person.Employment, (item,i)=>new CandidateEmployment {CandidateId=person.Id,SortOrder=i,CompanyName=Text(item,"company_name","CompanyName"),Designation=Text(item,"designation","Designation"),StartDate=Text(item,"start_date","StartDate"),EndDate=Text(item,"end_date","EndDate"),Location=Text(item,"location","Location")}).Where(x=>x.CompanyName.Length>0||x.Designation.Length>0).ToList();
        if (person.EducationHistory.Count==0) person.EducationHistory=Parse(person.Education, (item,i)=>new CandidateEducation {CandidateId=person.Id,SortOrder=i,Qualification=Text(item,"qualification","Qualification"),Institution=Text(item,"institution","Institution"),StartDate=Text(item,"start_date","StartDate"),EndDate=Text(item,"end_date","EndDate"),Location=Text(item,"location","Location")}).Where(x=>x.Qualification.Length>0).ToList();
    }

    static List<T> Parse<T>(string value, Func<JsonElement,int,T> map)
    {
        if (string.IsNullOrWhiteSpace(value) || !value.TrimStart().StartsWith('[')) return [];
        try { using var doc=JsonDocument.Parse(value); return doc.RootElement.EnumerateArray().Where(x=>x.ValueKind==JsonValueKind.Object).Select(map).ToList(); }
        catch (JsonException) { return []; } // Legacy text/malformed JSON remains available, never fabricated.
    }

    public static CandidateEmployment? LatestEmployment(IEnumerable<CandidateEmployment> history)
    {
        var rows=history.OrderBy(x=>x.SortOrder).ToList();
        return rows.FirstOrDefault(x=>IsCurrent(x.EndDate)) ?? rows.Where(x=>ParseDate(x.EndDate) is not null).OrderByDescending(x=>ParseDate(x.EndDate)).FirstOrDefault();
    }

    public static void ReconcileCurrentEmployment(Candidate person)
    {
        var latest=LatestEmployment(person.EmploymentHistory);
        if (latest is null) return;
        person.CurrentRole=latest.Designation;
        person.CurrentCompany=latest.CompanyName;
    }

    public static bool IsCurrent(string? date) => new[]{"present","current","ongoing","till date","to date","now"}.Contains(date?.Trim().ToLowerInvariant());

    static DateTime? ParseDate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || IsCurrent(text)) return null;
        text=text.Trim();
        if (text.Length==4 && int.TryParse(text,out var year) && year is >0 and <10000) return new DateTime(year,12,31);
        if (DateTime.TryParseExact(text,["d/M/yyyy","d-M-yyyy","d.M.yyyy","M/yyyy","M-yyyy","yyyy-MM-dd","MMM yyyy","MMMM yyyy"],CultureInfo.InvariantCulture,DateTimeStyles.None,out var exact)) return exact;
        return DateTime.TryParse(text,CultureInfo.InvariantCulture,DateTimeStyles.None,out var date)?date:null;
    }

    public static List<ScoredCriterion> HistoricalCriteria(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try {
            using var doc=JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind!=JsonValueKind.Array) throw new InvalidOperationException("Criterion Results JSON must be an array.");
            return doc.RootElement.EnumerateArray().Select(item=>new ScoredCriterion(Text(item,"criterion","Name"), Integer(item,"weight","Weight")??0, Text(item,"knockout").Equals("Yes",StringComparison.OrdinalIgnoreCase)||(item.TryGetProperty("Knockout",out var k)&&k.ValueKind==JsonValueKind.True),Integer(item,"score10","score_10","Score"),Integer(item,"evidenceConfidence10","evidence_confidence_10","Confidence"),Text(item,"evidenceStatus","Status"),Text(item,"evidenceFound","evidence_found","Found"),Text(item,"assessmentRationale","assessment_rationale","Rationale"))).ToList();
        } catch (JsonException) { throw new InvalidOperationException("Stored criterion evidence contains invalid JSON. Correct the source row before importing."); }
    }

    static int? Integer(JsonElement item,params string[] names)
    {
        foreach (var n in names) if (item.TryGetProperty(n,out var p)) {
            if (p.ValueKind==JsonValueKind.Number && p.TryGetInt32(out var number)) return number;
            if (p.ValueKind==JsonValueKind.String && int.TryParse(p.GetString(),out number)) return number;
        }
        return null;
    }
}
