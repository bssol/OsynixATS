using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Osynix.Ats.Data;

#nullable disable

namespace Osynix.Ats.Data.Migrations;

[DbContext(typeof(AtsDbContext))]
[Migration("20261026160000_ClientOperationsPortal")]
public partial class ClientOperationsPortal : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(name: "HiringManagerPortalEnabled", table: "Positions", type: "INTEGER", nullable: false, defaultValue: false);
        migrationBuilder.AddColumn<Guid>(name: "PositionId", table: "HiringReviewGrants", type: "TEXT", nullable: true);
        migrationBuilder.AddColumn<Guid>(name: "ContactId", table: "HiringReviewGrants", type: "TEXT", nullable: true);
        migrationBuilder.AddColumn<bool>(name: "PortalEnabled", table: "ClientContacts", type: "INTEGER", nullable: false, defaultValue: false);

        migrationBuilder.CreateTable(
            name: "ClientPortalMessages",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                GrantId = table.Column<Guid>(type: "TEXT", nullable: false),
                PositionId = table.Column<Guid>(type: "TEXT", nullable: false),
                FromClient = table.Column<bool>(type: "INTEGER", nullable: false),
                SenderName = table.Column<string>(type: "TEXT", nullable: false),
                Body = table.Column<string>(type: "TEXT", nullable: false),
                ReadUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                UpdatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                Revision = table.Column<Guid>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ClientPortalMessages", x => x.Id);
                table.ForeignKey("FK_ClientPortalMessages_HiringReviewGrants_GrantId", x => x.GrantId, "HiringReviewGrants", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_ClientPortalMessages_Positions_PositionId", x => x.PositionId, "Positions", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "PortalEmailDeliveries",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                Key = table.Column<string>(type: "TEXT", nullable: false),
                Email = table.Column<string>(type: "TEXT", nullable: false),
                Subject = table.Column<string>(type: "TEXT", nullable: false),
                Body = table.Column<string>(type: "TEXT", nullable: false),
                RequiresPublicAddress = table.Column<bool>(type: "INTEGER", nullable: false),
                GrantId = table.Column<Guid>(type: "TEXT", nullable: true),
                Status = table.Column<string>(type: "TEXT", nullable: false),
                Attempts = table.Column<int>(type: "INTEGER", nullable: false),
                SentUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                NextAttemptUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                UpdatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                Revision = table.Column<Guid>(type: "TEXT", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_PortalEmailDeliveries", x => x.Id));

        migrationBuilder.CreateIndex(name: "IX_ClientPortalMessages_GrantId_CreatedUtc", table: "ClientPortalMessages", columns: new[] { "GrantId", "CreatedUtc" });
        migrationBuilder.CreateIndex(name: "IX_ClientPortalMessages_PositionId", table: "ClientPortalMessages", column: "PositionId");
        migrationBuilder.CreateIndex(name: "IX_PortalEmailDeliveries_Key", table: "PortalEmailDeliveries", column: "Key", unique: true);

        migrationBuilder.Sql("UPDATE SubscriptionPlans SET TermsJson = json_set(TermsJson, '$.HiringManagerReviews', CASE WHEN lower(Code) = 'free' THEN json('false') ELSE json('true') END)");
        migrationBuilder.Sql("UPDATE EmployerWorkspaces SET SubscriptionTermsJson = (SELECT json_set(p.TermsJson, '$.HiringManagerReviews', CASE WHEN lower(p.Code) = 'free' THEN json('false') ELSE json('true') END) FROM SubscriptionPlans p WHERE p.Id = EmployerWorkspaces.SubscriptionPlanId) WHERE SubscriptionPlanId IS NOT NULL");
        migrationBuilder.Sql("UPDATE ClientContacts SET PortalEnabled = 1 WHERE EXISTS (SELECT 1 FROM HiringReviewGrants g WHERE g.ClientId = ClientContacts.ClientId AND g.EmailKey = lower(trim(ClientContacts.Email)) AND g.Revoked = 0)");
        migrationBuilder.Sql("UPDATE Positions SET HiringManagerPortalEnabled = 1 WHERE EXISTS (SELECT 1 FROM HiringReviewGrants g WHERE g.ClientId = Positions.ClientId AND g.Revoked = 0 AND (g.PositionId = Positions.Id OR g.PositionId IS NULL)) AND HiringManagerEmail <> ''");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("ClientPortalMessages");
        migrationBuilder.DropTable("PortalEmailDeliveries");
        migrationBuilder.Sql("ALTER TABLE HiringReviewGrants DROP COLUMN ContactId");
        migrationBuilder.Sql("ALTER TABLE HiringReviewGrants DROP COLUMN PositionId");
        migrationBuilder.Sql("ALTER TABLE ClientContacts DROP COLUMN PortalEnabled");
        migrationBuilder.Sql("ALTER TABLE Positions DROP COLUMN HiringManagerPortalEnabled");
    }
}
