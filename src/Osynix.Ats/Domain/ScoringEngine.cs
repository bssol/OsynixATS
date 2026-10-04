namespace Osynix.Ats.Domain;

// Port of the deterministic functions in OpenAI(4).txt. The model never decides the total or knockout status.
public static class ScoringEngine
{
    public static void ValidateCriteria(IReadOnlyList<Criterion> criteria)
    {
        if (criteria.Count == 0 || criteria.Any(x => string.IsNullOrWhiteSpace(x.Name) || x.Weight <= 0) || criteria.Sum(x => x.Weight) != 100)
            throw new InvalidOperationException("Criteria need names, positive weights and a total weight of exactly 100%.");
        if (criteria.Select(x => Choices.Normalize(x.Name)).Distinct().Count() != criteria.Count)
            throw new InvalidOperationException("Criterion names must be unique within a position.");
    }
    public static ScoringResult Calculate(IReadOnlyList<Criterion> criteria, IReadOnlyList<Evidence> evidence, IReadOnlyList<DecisionRule> rules)
    {
        ValidateCriteria(criteria);
        var scored = criteria.Select(c => {
            var matches = evidence.Where(e => Choices.Normalize(e.Criterion) == Choices.Normalize(c.Name)).ToList();
            var e = matches.Count == 1 ? matches[0] : new Evidence(c.Name, 0, 2, "Not evidenced: missing or ambiguous criterion result.", "No positive score credited.");
            var score = Snap(e.Score, [0,2,4,6,8,10]);
            var confidence = Snap(e.Confidence, [2,4,6,8,10]);
            var status = score == 0 && confidence <= 2 ? "Insufficient Evidence" : score <= 2 ? "Fail" : score == 4 ? "Partial" : "Pass";
            return new ScoredCriterion(c.Name, c.Weight, c.Knockout, score, confidence, status, e.Found, e.Rationale);
        }).ToList();
        var knockout = scored.Where(x => x.Knockout).ToList();
        var fit = new[]{"Fail", "Insufficient Evidence", "Partial"}.FirstOrDefault(s => knockout.Any(x => x.Status == s)) ?? "Pass";
        var match = (int)Math.Round(scored.Sum(x => x.Weight * x.Score / 10d), MidpointRounding.AwayFromZero);
        var core = scored.Where(x => !x.Knockout).ToList();
        if (core.Count == 0) core = scored;
        var decision = fit == "Fail" ? "Reject" : fit == "Insufficient Evidence" ? "Insufficient Evidence" : rules.Where(r => r.MustHaveFit == fit && match >= r.MinimumMatch).OrderByDescending(r => r.MinimumMatch).FirstOrDefault()?.Decision ?? "Hold";
        return new(match, fit, Math.Round(core.Sum(x=>x.Weight*x.Score)/(double)core.Sum(x=>x.Weight),1), Math.Round(scored.Sum(x=>x.Weight*x.Confidence)/100d,1), decision, scored);
    }
    static int Snap(int value, int[] allowed) => allowed.OrderBy(x => Math.Abs(x-value)).First();
    public static string InitialStage(string decision) => decision.Contains("Shortlist", StringComparison.OrdinalIgnoreCase) ? "Shortlisted" : decision == "Reject" ? "Rejected" : "Hold";
    public static string Message(string name, string title, string decision) => decision.Contains("Shortlist", StringComparison.OrdinalIgnoreCase)
        ? $"Dear {name},\n\nThank you for your interest in the {title} position. We would like to arrange an interview. Please share your interview availability, current compensation (salary plus perks and benefits), expected salary range, and notice period.\n\nOsynix Group"
        : decision == "Reject" ? $"Dear {name},\n\nThank you for your interest in the {title} position. We will not be progressing your application for this role. We appreciate your time and will retain your profile for relevant opportunities.\n\nOsynix Group"
        : $"Dear {name},\n\nThank you for your interest in the {title} position. Your application is on hold while our recruitment team reviews the requirements. We will contact you if there is a suitable next step.\n\nOsynix Group";
}
