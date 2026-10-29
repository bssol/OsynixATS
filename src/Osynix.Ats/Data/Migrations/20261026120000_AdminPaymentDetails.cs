using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Osynix.Ats.Data.Migrations;

[DbContext(typeof(AtsDbContext))]
[Migration("20261026120000_AdminPaymentDetails")]
public sealed class AdminPaymentDetailsMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "AdminPaymentDetails",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                BankName = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                AccountTitle = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                AccountNumber = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                Iban = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                Branch = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                PaymentInstructions = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                UpdatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                Revision = table.Column<Guid>(type: "TEXT", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_AdminPaymentDetails", x => x.Id));
    }

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable(name: "AdminPaymentDetails");
}
