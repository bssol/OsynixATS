using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Osynix.Ats.Data.Migrations
{
    /// <inheritdoc />
    public partial class RestoreBusinessWorkflows : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Assessments_CandidateKey_PositionId",
                table: "Assessments");

            migrationBuilder.AddColumn<string>(
                name: "AssignedRecruiter",
                table: "Positions",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "ClientId",
                table: "Positions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClosedBy",
                table: "Positions",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "ClosedDate",
                table: "Positions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClosureStatus",
                table: "Positions",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "Positions",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "FilledSource",
                table: "Positions",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "HiringContact",
                table: "Positions",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "HiringTarget",
                table: "Positions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<Guid>(
                name: "JdDocumentId",
                table: "Positions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "JdHash",
                table: "Positions",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "LastUpdatedBy",
                table: "Positions",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "LegacyJdLink",
                table: "Positions",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "OpenDate",
                table: "Positions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PositionSummary",
                table: "Positions",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Priority",
                table: "Positions",
                type: "TEXT",
                nullable: false,
                defaultValue: "Medium");

            migrationBuilder.AddColumn<string>(
                name: "ContentHash",
                table: "Documents",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Rationale",
                table: "Criteria",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "CareerSummary",
                table: "Candidates",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "FirstScreenedDate",
                table: "Candidates",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IntelligenceJson",
                table: "Candidates",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastAssessedDate",
                table: "Candidates",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NormalizedPhone",
                table: "Candidates",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ActorName",
                table: "Audit",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "AssessmentId",
                table: "Audit",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CandidateId",
                table: "Audit",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EntityType",
                table: "Audit",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "NewJson",
                table: "Audit",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PreviousJson",
                table: "Audit",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AlterColumn<int>(
                name: "MatchPercent",
                table: "Assessments",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<double>(
                name: "EvidenceStrength",
                table: "Assessments",
                type: "REAL",
                nullable: true,
                oldClrType: typeof(double),
                oldType: "REAL");

            migrationBuilder.AlterColumn<Guid>(
                name: "DocumentId",
                table: "Assessments",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT");

            migrationBuilder.AlterColumn<double>(
                name: "CoreRoleFit",
                table: "Assessments",
                type: "REAL",
                nullable: true,
                oldClrType: typeof(double),
                oldType: "REAL");

            migrationBuilder.AddColumn<Guid>(
                name: "BatchId",
                table: "Assessments",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BatchIndex",
                table: "Assessments",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "DateScreened",
                table: "Assessments",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastAssessedDate",
                table: "Assessments",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PolicyJson",
                table: "Assessments",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "RefreshProfile",
                table: "Assessments",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "ReplacedByAssessmentId",
                table: "Assessments",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SourceCandidateId",
                table: "Assessments",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceFileName",
                table: "Assessments",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "VersionNumber",
                table: "Assessments",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<bool>(
                name: "CanCreatePositions",
                table: "AspNetUsers",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "CanExportReports",
                table: "AspNetUsers",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "CanViewTalentPool",
                table: "AspNetUsers",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "FullName",
                table: "AspNetUsers",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "AspNetUsers",
                type: "TEXT",
                nullable: false,
                defaultValue: "Active");

            migrationBuilder.CreateTable(
                name: "AssessmentBatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PositionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", nullable: false),
                    Mode = table.Column<string>(type: "TEXT", nullable: false),
                    State = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Revision = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssessmentBatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssessmentBatches_Positions_PositionId",
                        column: x => x.PositionId,
                        principalTable: "Positions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AssessmentVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AssessmentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Number = table.Column<int>(type: "INTEGER", nullable: false),
                    SnapshotJson = table.Column<string>(type: "TEXT", nullable: false),
                    MatchPercent = table.Column<int>(type: "INTEGER", nullable: true),
                    MustHaveFit = table.Column<string>(type: "TEXT", nullable: false),
                    CoreRoleFit = table.Column<double>(type: "REAL", nullable: true),
                    EvidenceStrength = table.Column<double>(type: "REAL", nullable: true),
                    Decision = table.Column<string>(type: "TEXT", nullable: false),
                    Model = table.Column<string>(type: "TEXT", nullable: false),
                    PolicyJson = table.Column<string>(type: "TEXT", nullable: false),
                    DocumentId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Revision = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssessmentVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssessmentVersions_Assessments_AssessmentId",
                        column: x => x.AssessmentId,
                        principalTable: "Assessments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CandidateNotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CandidateId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AssessmentId = table.Column<Guid>(type: "TEXT", nullable: true),
                    AuthorId = table.Column<string>(type: "TEXT", nullable: false),
                    AuthorName = table.Column<string>(type: "TEXT", nullable: false),
                    Text = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Revision = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CandidateNotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CandidateNotes_Assessments_AssessmentId",
                        column: x => x.AssessmentId,
                        principalTable: "Assessments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CandidateNotes_Candidates_CandidateId",
                        column: x => x.CandidateId,
                        principalTable: "Candidates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Clients",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    NormalizedName = table.Column<string>(type: "TEXT", nullable: false),
                    Industry = table.Column<string>(type: "TEXT", nullable: false),
                    Website = table.Column<string>(type: "TEXT", nullable: false),
                    Location = table.Column<string>(type: "TEXT", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Revision = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Clients", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EducationHistory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CandidateId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    Qualification = table.Column<string>(type: "TEXT", nullable: false),
                    Institution = table.Column<string>(type: "TEXT", nullable: true),
                    StartDate = table.Column<string>(type: "TEXT", nullable: true),
                    EndDate = table.Column<string>(type: "TEXT", nullable: true),
                    Location = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Revision = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EducationHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EducationHistory_Candidates_CandidateId",
                        column: x => x.CandidateId,
                        principalTable: "Candidates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EmploymentHistory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CandidateId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    CompanyName = table.Column<string>(type: "TEXT", nullable: false),
                    Designation = table.Column<string>(type: "TEXT", nullable: false),
                    StartDate = table.Column<string>(type: "TEXT", nullable: true),
                    EndDate = table.Column<string>(type: "TEXT", nullable: true),
                    Location = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Revision = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmploymentHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmploymentHistory_Candidates_CandidateId",
                        column: x => x.CandidateId,
                        principalTable: "Candidates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReferenceOptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    List = table.Column<string>(type: "TEXT", nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: false),
                    Order = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Revision = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReferenceOptions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BatchItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BatchId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Index = table.Column<int>(type: "INTEGER", nullable: false),
                    FileName = table.Column<string>(type: "TEXT", nullable: false),
                    State = table.Column<string>(type: "TEXT", nullable: false),
                    Error = table.Column<string>(type: "TEXT", nullable: false),
                    AssessmentId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Revision = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BatchItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BatchItems_AssessmentBatches_BatchId",
                        column: x => x.BatchId,
                        principalTable: "AssessmentBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ClientContacts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    Email = table.Column<string>(type: "TEXT", nullable: false),
                    Phone = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Revision = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientContacts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClientContacts_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Positions_ClientId",
                table: "Positions",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_Positions_JdDocumentId",
                table: "Positions",
                column: "JdDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_Documents_ContentHash",
                table: "Documents",
                column: "ContentHash");

            migrationBuilder.CreateIndex(
                name: "IX_Candidates_DocumentId",
                table: "Candidates",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_Candidates_LegacyId",
                table: "Candidates",
                column: "LegacyId",
                unique: true,
                filter: "\"LegacyId\" <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_Candidates_NormalizedPhone",
                table: "Candidates",
                column: "NormalizedPhone");

            migrationBuilder.CreateIndex(
                name: "IX_Assessments_BatchId",
                table: "Assessments",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_Assessments_CandidateKey_PositionId",
                table: "Assessments",
                columns: new[] { "CandidateKey", "PositionId" });

            migrationBuilder.CreateIndex(
                name: "IX_Assessments_ReplacedByAssessmentId",
                table: "Assessments",
                column: "ReplacedByAssessmentId");

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentBatches_PositionId",
                table: "AssessmentBatches",
                column: "PositionId");

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentVersions_AssessmentId_Number",
                table: "AssessmentVersions",
                columns: new[] { "AssessmentId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BatchItems_BatchId",
                table: "BatchItems",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_CandidateNotes_AssessmentId",
                table: "CandidateNotes",
                column: "AssessmentId");

            migrationBuilder.CreateIndex(
                name: "IX_CandidateNotes_CandidateId",
                table: "CandidateNotes",
                column: "CandidateId");

            migrationBuilder.CreateIndex(
                name: "IX_ClientContacts_ClientId",
                table: "ClientContacts",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_Clients_NormalizedName",
                table: "Clients",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EducationHistory_CandidateId",
                table: "EducationHistory",
                column: "CandidateId");

            migrationBuilder.CreateIndex(
                name: "IX_EmploymentHistory_CandidateId",
                table: "EmploymentHistory",
                column: "CandidateId");

            migrationBuilder.CreateIndex(
                name: "IX_ReferenceOptions_List_Value",
                table: "ReferenceOptions",
                columns: new[] { "List", "Value" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Assessments_AssessmentBatches_BatchId",
                table: "Assessments",
                column: "BatchId",
                principalTable: "AssessmentBatches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Assessments_Assessments_ReplacedByAssessmentId",
                table: "Assessments",
                column: "ReplacedByAssessmentId",
                principalTable: "Assessments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Candidates_Documents_DocumentId",
                table: "Candidates",
                column: "DocumentId",
                principalTable: "Documents",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Positions_Clients_ClientId",
                table: "Positions",
                column: "ClientId",
                principalTable: "Clients",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Positions_Documents_JdDocumentId",
                table: "Positions",
                column: "JdDocumentId",
                principalTable: "Documents",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Assessments_AssessmentBatches_BatchId",
                table: "Assessments");

            migrationBuilder.DropForeignKey(
                name: "FK_Assessments_Assessments_ReplacedByAssessmentId",
                table: "Assessments");

            migrationBuilder.DropForeignKey(
                name: "FK_Candidates_Documents_DocumentId",
                table: "Candidates");

            migrationBuilder.DropForeignKey(
                name: "FK_Positions_Clients_ClientId",
                table: "Positions");

            migrationBuilder.DropForeignKey(
                name: "FK_Positions_Documents_JdDocumentId",
                table: "Positions");

            migrationBuilder.DropTable(
                name: "AssessmentVersions");

            migrationBuilder.DropTable(
                name: "BatchItems");

            migrationBuilder.DropTable(
                name: "CandidateNotes");

            migrationBuilder.DropTable(
                name: "ClientContacts");

            migrationBuilder.DropTable(
                name: "EducationHistory");

            migrationBuilder.DropTable(
                name: "EmploymentHistory");

            migrationBuilder.DropTable(
                name: "ReferenceOptions");

            migrationBuilder.DropTable(
                name: "AssessmentBatches");

            migrationBuilder.DropTable(
                name: "Clients");

            migrationBuilder.DropIndex(
                name: "IX_Positions_ClientId",
                table: "Positions");

            migrationBuilder.DropIndex(
                name: "IX_Positions_JdDocumentId",
                table: "Positions");

            migrationBuilder.DropIndex(
                name: "IX_Documents_ContentHash",
                table: "Documents");

            migrationBuilder.DropIndex(
                name: "IX_Candidates_DocumentId",
                table: "Candidates");

            migrationBuilder.DropIndex(
                name: "IX_Candidates_LegacyId",
                table: "Candidates");

            migrationBuilder.DropIndex(
                name: "IX_Candidates_NormalizedPhone",
                table: "Candidates");

            migrationBuilder.DropIndex(
                name: "IX_Assessments_BatchId",
                table: "Assessments");

            migrationBuilder.DropIndex(
                name: "IX_Assessments_CandidateKey_PositionId",
                table: "Assessments");

            migrationBuilder.DropIndex(
                name: "IX_Assessments_ReplacedByAssessmentId",
                table: "Assessments");

            migrationBuilder.DropColumn(
                name: "AssignedRecruiter",
                table: "Positions");

            migrationBuilder.DropColumn(
                name: "ClientId",
                table: "Positions");

            migrationBuilder.DropColumn(
                name: "ClosedBy",
                table: "Positions");

            migrationBuilder.DropColumn(
                name: "ClosedDate",
                table: "Positions");

            migrationBuilder.DropColumn(
                name: "ClosureStatus",
                table: "Positions");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "Positions");

            migrationBuilder.DropColumn(
                name: "FilledSource",
                table: "Positions");

            migrationBuilder.DropColumn(
                name: "HiringContact",
                table: "Positions");

            migrationBuilder.DropColumn(
                name: "HiringTarget",
                table: "Positions");

            migrationBuilder.DropColumn(
                name: "JdDocumentId",
                table: "Positions");

            migrationBuilder.DropColumn(
                name: "JdHash",
                table: "Positions");

            migrationBuilder.DropColumn(
                name: "LastUpdatedBy",
                table: "Positions");

            migrationBuilder.DropColumn(
                name: "LegacyJdLink",
                table: "Positions");

            migrationBuilder.DropColumn(
                name: "OpenDate",
                table: "Positions");

            migrationBuilder.DropColumn(
                name: "PositionSummary",
                table: "Positions");

            migrationBuilder.DropColumn(
                name: "Priority",
                table: "Positions");

            migrationBuilder.DropColumn(
                name: "ContentHash",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "Rationale",
                table: "Criteria");

            migrationBuilder.DropColumn(
                name: "CareerSummary",
                table: "Candidates");

            migrationBuilder.DropColumn(
                name: "FirstScreenedDate",
                table: "Candidates");

            migrationBuilder.DropColumn(
                name: "IntelligenceJson",
                table: "Candidates");

            migrationBuilder.DropColumn(
                name: "LastAssessedDate",
                table: "Candidates");

            migrationBuilder.DropColumn(
                name: "NormalizedPhone",
                table: "Candidates");

            migrationBuilder.DropColumn(
                name: "ActorName",
                table: "Audit");

            migrationBuilder.DropColumn(
                name: "AssessmentId",
                table: "Audit");

            migrationBuilder.DropColumn(
                name: "CandidateId",
                table: "Audit");

            migrationBuilder.DropColumn(
                name: "EntityType",
                table: "Audit");

            migrationBuilder.DropColumn(
                name: "NewJson",
                table: "Audit");

            migrationBuilder.DropColumn(
                name: "PreviousJson",
                table: "Audit");

            migrationBuilder.DropColumn(
                name: "BatchId",
                table: "Assessments");

            migrationBuilder.DropColumn(
                name: "BatchIndex",
                table: "Assessments");

            migrationBuilder.DropColumn(
                name: "DateScreened",
                table: "Assessments");

            migrationBuilder.DropColumn(
                name: "LastAssessedDate",
                table: "Assessments");

            migrationBuilder.DropColumn(
                name: "PolicyJson",
                table: "Assessments");

            migrationBuilder.DropColumn(
                name: "RefreshProfile",
                table: "Assessments");

            migrationBuilder.DropColumn(
                name: "ReplacedByAssessmentId",
                table: "Assessments");

            migrationBuilder.DropColumn(
                name: "SourceCandidateId",
                table: "Assessments");

            migrationBuilder.DropColumn(
                name: "SourceFileName",
                table: "Assessments");

            migrationBuilder.DropColumn(
                name: "VersionNumber",
                table: "Assessments");

            migrationBuilder.DropColumn(
                name: "CanCreatePositions",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "CanExportReports",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "CanViewTalentPool",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "FullName",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "AspNetUsers");

            migrationBuilder.AlterColumn<int>(
                name: "MatchPercent",
                table: "Assessments",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true);

            migrationBuilder.AlterColumn<double>(
                name: "EvidenceStrength",
                table: "Assessments",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0,
                oldClrType: typeof(double),
                oldType: "REAL",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "DocumentId",
                table: "Assessments",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true);

            migrationBuilder.AlterColumn<double>(
                name: "CoreRoleFit",
                table: "Assessments",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0,
                oldClrType: typeof(double),
                oldType: "REAL",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Assessments_CandidateKey_PositionId",
                table: "Assessments",
                columns: new[] { "CandidateKey", "PositionId" },
                unique: true);
        }
    }
}
