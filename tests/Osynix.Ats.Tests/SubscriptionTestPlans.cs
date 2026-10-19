using Microsoft.EntityFrameworkCore;
using Osynix.Ats.Data;
using Osynix.Ats.Services;
namespace Osynix.Ats.Tests;

internal static class SubscriptionTestPlans
{
    // Marketplace regression scenarios require a paid Freelancer entitlement.
    public static async Task Marketplace(AtsDbContext db,string userId)
    {
        var user=await db.Users.SingleAsync(u=>u.Id==userId);
        var workspace=await EmployerService.EnsureWorkspace(db,user);
        workspace=db.EmployerWorkspaces.Local.FirstOrDefault(w=>w.Id==workspace.Id)??workspace;
        if(db.Entry(workspace).State==EntityState.Detached)db.Attach(workspace);
        var starter=await db.SubscriptionPlans.SingleAsync(p=>p.AccountCategory=="Freelancer"&&p.Code=="starter");
        SubscriptionRules.Apply(workspace,starter);await db.SaveChangesAsync();
    }
}
