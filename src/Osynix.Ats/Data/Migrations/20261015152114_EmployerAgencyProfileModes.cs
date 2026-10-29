using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Osynix.Ats.Data.Migrations
{
    /// <inheritdoc />
    public partial class EmployerAgencyProfileModes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsAgencyPosition",
                table: "Positions",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
            migrationBuilder.Sql("UPDATE Positions SET IsAgencyPosition = 1 WHERE EXISTS (SELECT 1 FROM EmployerWorkspaces w JOIN Clients c ON c.Id = Positions.ClientId WHERE w.Id = Positions.WorkspaceId AND w.Kind = 'Employer' AND w.AgencyEnabled = 1 AND lower(trim(c.Name)) <> lower(trim(w.CompanyName)))");

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsAgencyPosition",
                table: "Positions");
        }
    }
}
