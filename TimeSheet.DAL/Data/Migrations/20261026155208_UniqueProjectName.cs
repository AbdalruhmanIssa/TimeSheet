using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TimeSheet.DAL.Data.Migrations
{
    /// <inheritdoc />
    public partial class UniqueProjectName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing databases may already hold duplicate names (created before this rule).
            // Keep the oldest as-is and rename the others to "<Name> (<Code>)" so the unique
            // index can be created. Nothing is deleted. Same collation as the index, so
            // "acme" and "ACME" count as duplicates here too.
            migrationBuilder.Sql(@"
WITH Ranked AS (
    SELECT Id, ROW_NUMBER() OVER (PARTITION BY Name ORDER BY CreatedAt, Code) AS Rn
    FROM Projects
)
UPDATE p
SET Name = LEFT(p.Name, 200 - LEN(p.Code) - 3) + ' (' + p.Code + ')'
FROM Projects p
JOIN Ranked r ON r.Id = p.Id
WHERE r.Rn > 1;");

            migrationBuilder.CreateIndex(
                name: "IX_Projects_Name",
                table: "Projects",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Projects_Name",
                table: "Projects");
        }
    }
}
