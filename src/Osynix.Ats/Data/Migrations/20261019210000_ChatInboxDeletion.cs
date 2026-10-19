using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Osynix.Ats.Data.Migrations;

[DbContext(typeof(AtsDbContext))]
[Migration("20261019210000_ChatInboxDeletion")]
public class ChatInboxDeletionMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(name:"ChatInboxDeletions",columns:table=>new{
            UserId=table.Column<string>(type:"TEXT",nullable:false),
            Kind=table.Column<string>(type:"TEXT",nullable:false),
            ConversationId=table.Column<Guid>(type:"TEXT",nullable:false),
            DeletedThroughUtc=table.Column<DateTime>(type:"TEXT",nullable:false)
        },constraints:table=>{
            table.PrimaryKey("PK_ChatInboxDeletions",x=>new{x.UserId,x.Kind,x.ConversationId});
            table.ForeignKey("FK_ChatInboxDeletions_AspNetUsers_UserId",x=>x.UserId,"AspNetUsers","Id",onDelete:ReferentialAction.Restrict);
        });
    }
    protected override void Down(MigrationBuilder migrationBuilder)=>migrationBuilder.DropTable("ChatInboxDeletions");
}
