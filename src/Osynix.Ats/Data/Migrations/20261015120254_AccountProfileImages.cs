using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Osynix.Ats.Data.Migrations
{
    /// <inheritdoc />
    public partial class AccountProfileImages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "ProfileImage",
                table: "AspNetUsers",
                type: "BLOB",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProfileImageType",
                table: "AspNetUsers",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProfileImage",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "ProfileImageType",
                table: "AspNetUsers");
        }
    }
}
