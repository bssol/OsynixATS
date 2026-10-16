using Microsoft.EntityFrameworkCore;
using Osynix.Ats.Data;
using Osynix.Ats.Domain;
namespace Osynix.Ats.Services;
public static class CandidateCityUpgrade {
 public static async Task Apply(AtsDbContext db){foreach(var person in await db.Candidates.Where(c=>c.City=="").ToListAsync()){var city=CandidateCity.FromLegacy(person.Location);if(city.Length==0)continue;person.City=city;AtsService.Audit(db,"System","Candidate city normalized",person.Id.ToString(),"City-Only Display Updated; Original CV Preserved",person.Id);}await db.SaveChangesAsync();}
}
