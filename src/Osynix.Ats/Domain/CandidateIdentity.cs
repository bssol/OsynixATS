using System.Text.RegularExpressions;

namespace Osynix.Ats.Domain;

public static class CandidateIdentity
{
    public static string Email(string value) => value.Trim().ToLowerInvariant();
    public static string Phone(string value) => Regex.Replace(value.Split(['/',',',';'],2)[0], "\\D", "");

    public static Candidate? Resolve(IEnumerable<Candidate> people, Candidate incoming)
    {
        var all=people.ToList();
        var email=Email(incoming.Email); var phone=Phone(incoming.Phone); var name=Choices.MatchKey(incoming.Name);
        var emailMatches=email.Length==0 ? [] : all.Where(c=>Email(c.Email)==email).ToList();
        var phoneMatches=phone.Length==0 ? [] : all.Where(c=>Phone(c.Phone)==phone).ToList();
        if (emailMatches.Count>1 || phoneMatches.Count>1) throw new InvalidOperationException("Multiple profiles share this contact. Resolve the identity conflict before saving.");
        if (emailMatches.Count==1) {
            if (phoneMatches.Count==1 && phoneMatches[0].Id!=emailMatches[0].Id) throw new InvalidOperationException("Email and phone identify different people. Resolve this conflict before saving.");
            return emailMatches[0];
        }
        if (phoneMatches.Count==1) return phoneMatches[0];
        if(name.Length==0) return null;
        var names=all.Where(c=>Choices.MatchKey(c.Name)==name).ToList();
        if (names.Count>1) throw new InvalidOperationException("This name matches several people. Supply a matching email/phone or select the existing candidate.");
        var match=names.SingleOrDefault();
        // New, different contacts distinguish two people with the same name.
        if (match is not null && ((email.Length>0 && match.Email.Length>0 && Email(match.Email)!=email) || (phone.Length>0 && match.Phone.Length>0 && Phone(match.Phone)!=phone))) return null;
        return match;
    }

    public static string NextDisplayId(IEnumerable<string> existing)
    {
        var used=existing.ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var i=1; i<int.MaxValue; i++) if (!used.Contains($"CAND-{i:000}")) return $"CAND-{i:000}";
        throw new InvalidOperationException("Candidate sequence exhausted.");
    }
}
