using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Osynix.Ats.Data.Migrations
{
    /// <inheritdoc />
    public partial class SharedClientReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ClientId",
                table: "HiringReviewGrants",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_HiringReviewGrants_ClientId",
                table: "HiringReviewGrants",
                column: "ClientId");
            migrationBuilder.Sql("""
                UPDATE HiringReviewGrants SET ClientId=(
                    SELECT MIN(p.ClientId) FROM HiringSubmissions s JOIN Assessments a ON a.Id=s.AssessmentId JOIN Positions p ON p.Id=a.PositionId WHERE s.GrantId=HiringReviewGrants.Id
                ) WHERE (SELECT COUNT(DISTINCT p.ClientId) FROM HiringSubmissions s JOIN Assessments a ON a.Id=s.AssessmentId JOIN Positions p ON p.Id=a.PositionId WHERE s.GrantId=HiringReviewGrants.Id)=1;
                UPDATE HiringReviewGrants SET Name=(SELECT Name FROM Clients WHERE Clients.Id=HiringReviewGrants.ClientId) WHERE ClientId IS NOT NULL AND EXISTS(SELECT 1 FROM Clients WHERE Clients.Id=HiringReviewGrants.ClientId);
                UPDATE Assessments SET Stage='Awaiting Client Feedback', Revision='4C2A6ECA-210C-4918-874B-3DA03BA7E3EA', UpdatedUtc=strftime('%Y-%m-%d %H:%M:%f','now') WHERE ClientStatus='Submitted' AND Stage IN ('Shortlisted','HR Screening','Applied','Consider') AND EXISTS(SELECT 1 FROM HiringSubmissions s WHERE s.AssessmentId=Assessments.Id AND s.Feedback='Awaiting Feedback' AND s.Revoked=0);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_HiringReviewGrants_ClientId",
                table: "HiringReviewGrants");

            migrationBuilder.DropColumn(
                name: "ClientId",
                table: "HiringReviewGrants");
        }
    }
}
