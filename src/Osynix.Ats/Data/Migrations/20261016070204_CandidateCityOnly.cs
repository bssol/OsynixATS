using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Osynix.Ats.Data.Migrations
{
    /// <inheritdoc />
    public partial class CandidateCityOnly : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "City",
                table: "Candidates",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "City",
                table: "Candidates");
        }
    }
}
