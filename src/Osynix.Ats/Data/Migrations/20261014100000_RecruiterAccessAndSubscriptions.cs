using Microsoft.EntityFrameworkCore.Migrations;
namespace Osynix.Ats.Data.Migrations;
[Migration("20261014100000_RecruiterAccessAndSubscriptions")]
public partial class RecruiterAccessAndSubscriptions : Migration {
 protected override void Up(MigrationBuilder m){
  m.AddColumn<bool>("CanCreateClients","AspNetUsers",type:"INTEGER",nullable:false,defaultValue:false);
  m.AddColumn<DateTime>("TalentPoolPaidUntilUtc","AspNetUsers",type:"TEXT",nullable:true);
  m.Sql("UPDATE AspNetUsers SET CanCreatePositions=0,CanViewTalentPool=0 WHERE Id IN (SELECT ur.UserId FROM AspNetUserRoles ur JOIN AspNetRoles r ON r.Id=ur.RoleId WHERE r.Name='Recruiter') AND Id NOT IN (SELECT ur.UserId FROM AspNetUserRoles ur JOIN AspNetRoles r ON r.Id=ur.RoleId WHERE r.Name='Admin')");
 }
 protected override void Down(MigrationBuilder m){m.DropColumn("CanCreateClients","AspNetUsers");m.DropColumn("TalentPoolPaidUntilUtc","AspNetUsers");}
}
