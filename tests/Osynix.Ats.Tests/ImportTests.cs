using System.Security.Claims;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Osynix.Ats.Data;
using Osynix.Ats.Services;
using Xunit;
namespace Osynix.Ats.Tests;
public class ImportTests
{
    sealed class AdminState:AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync()=>Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity([new(ClaimTypes.Name,"admin@test.local"),new(ClaimTypes.NameIdentifier,"admin"),new(ClaimTypes.Role,"Admin")],"Test"))));
    }
    sealed class Factory(DbContextOptions<AtsDbContext> options):IDbContextFactory<AtsDbContext>{public AtsDbContext CreateDbContext()=>new(options);}
    static byte[] Workbook(bool duplicate=false)
    {
        using var wb=new XLWorkbook();var p=wb.AddWorksheet("Positions Master");p.Cell(1,1).Value="Ref #";p.Cell(1,2).Value="Position";p.Cell(1,3).Value="Client";p.Cell(2,1).Value="001";p.Cell(2,2).Value="Manager";p.Cell(2,3).Value="Test client";
        var c=wb.AddWorksheet("JD Criteria");c.Cell(1,1).Value="Ref #";c.Cell(1,2).Value="Criterion";c.Cell(1,3).Value="Weight %";c.Cell(1,4).Value="Knockout?";c.Cell(2,1).Value="001";c.Cell(2,2).Value="Industry";c.Cell(2,3).Value=100;c.Cell(2,4).Value="Yes";
        var t=wb.AddWorksheet("Talent Pool Master");t.Cell(1,1).Value="Candidate Name";t.Cell(1,2).Value="Unmapped attribute";t.Cell(2,1).Value="Test Candidate";t.Cell(2,2).Value="Must survive import";
        var a=wb.AddWorksheet("Candidates");a.Cell(1,1).Value="Candidate Name";a.Cell(1,2).Value="Ref #";a.Cell(1,3).Value="ATS Match %";a.Cell(1,4).Value="Decision";a.Cell(2,1).Value="Test Candidate";a.Cell(2,2).Value="001";a.Cell(2,3).Value="80%";a.Cell(2,4).Value="Shortlist";
        if(duplicate){a.Cell(3,1).Value="Test Candidate";a.Cell(3,2).Value="001";}
        var s=wb.AddWorksheet("Lists & Settings");s.Cell(1,13).Value="Must-Have Fit";s.Cell(1,14).Value="Minimum ATS";s.Cell(1,15).Value="Decision";s.Cell(2,13).Value="Pass";s.Cell(2,14).Value="80%";s.Cell(2,15).Value="Shortlist";
        using var stream=new MemoryStream();wb.SaveAs(stream);return stream.ToArray();
    }
    [Fact]public async Task ImportPreservesUnmappedRowsAndHistoricalScores()
    {
        await using var connection=new SqliteConnection("Data Source=:memory:");await connection.OpenAsync();var factory=new Factory(new DbContextOptionsBuilder<AtsDbContext>().UseSqlite(connection).Options);
        await using(var db=factory.CreateDbContext())await db.Database.EnsureCreatedAsync();
        var service=new WorkbookImport(factory,new Access(new AdminState()));var data=Workbook();var preview=await service.Preview(data);Assert.Equal(1,preview.Rows["Candidates"]);
        await service.Commit(data);await using var check=factory.CreateDbContext();Assert.Equal(80,(await check.Assessments.SingleAsync()).MatchPercent);Assert.False((await check.Positions.SingleAsync()).CriteriaLocked);Assert.Contains(await check.ImportRows.ToListAsync(),x=>x.Json.Contains("Must survive import"));
        Assert.Contains("already been imported",await service.Commit(data));
    }
    [Fact]public async Task DuplicatePairsRollBackEverything()
    {
        await using var connection=new SqliteConnection("Data Source=:memory:");await connection.OpenAsync();var factory=new Factory(new DbContextOptionsBuilder<AtsDbContext>().UseSqlite(connection).Options);
        await using(var db=factory.CreateDbContext())await db.Database.EnsureCreatedAsync();var service=new WorkbookImport(factory,new Access(new AdminState()));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>service.Commit(Workbook(true)));await using var check=factory.CreateDbContext();Assert.Empty(await check.Positions.ToListAsync());Assert.Empty(await check.ImportRows.ToListAsync());
    }
}
