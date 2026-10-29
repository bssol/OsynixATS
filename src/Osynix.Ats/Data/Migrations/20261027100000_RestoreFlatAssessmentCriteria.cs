using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Osynix.Ats.Data;

#nullable disable

namespace Osynix.Ats.Data.Migrations;

[DbContext(typeof(AtsDbContext))]
[Migration("20261027100000_RestoreFlatAssessmentCriteria")]
public partial class RestoreFlatAssessmentCriteria : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Convert any category-priority values back to the prior per-position 100% weight total.
        migrationBuilder.Sql("""
            WITH totals AS (
                SELECT PositionId, SUM(Weight) AS TotalWeight
                FROM Criteria
                GROUP BY PositionId
            ), normalized AS (
                SELECT c.Id,
                       MAX(1, CAST(100.0 * c.Weight / t.TotalWeight AS INTEGER)) AS BaseWeight,
                       ROW_NUMBER() OVER (
                           PARTITION BY c.PositionId
                           ORDER BY (100.0 * c.Weight / t.TotalWeight) - CAST(100.0 * c.Weight / t.TotalWeight AS INTEGER) DESC,
                                    c.Weight DESC, c.Id
                       ) AS RowNumber,
                       SUM(MAX(1, CAST(100.0 * c.Weight / t.TotalWeight AS INTEGER))) OVER (PARTITION BY c.PositionId) AS BaseTotal
                FROM Criteria c
                JOIN totals t ON t.PositionId = c.PositionId
                WHERE t.TotalWeight > 0 AND t.TotalWeight <> 100
            )
            UPDATE Criteria
            SET Weight = (
                SELECT CASE WHEN normalized.RowNumber = 1
                            THEN normalized.BaseWeight + (100 - normalized.BaseTotal)
                            ELSE normalized.BaseWeight END
                FROM normalized
                WHERE normalized.Id = Criteria.Id
            )
            WHERE Id IN (SELECT Id FROM normalized);
            """);
        migrationBuilder.Sql("ALTER TABLE Criteria DROP COLUMN Category;");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
        => migrationBuilder.AddColumn<string>(name: "Category", table: "Criteria", type: "TEXT", nullable: false, defaultValue: "");
}
