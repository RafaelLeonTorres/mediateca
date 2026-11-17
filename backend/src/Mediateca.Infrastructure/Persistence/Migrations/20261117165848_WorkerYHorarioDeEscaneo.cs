using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mediateca.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class _20261117165848_WorkerYHorarioDeEscaneo : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ScanSchedules",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false),
                Enabled = table.Column<bool>(type: "boolean", nullable: false),
                CronExpression = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ScanSchedules", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ScanJobs_LibraryId_Active",
            table: "ScanJobs",
            column: "LibraryId",
            unique: true,
            filter: "\"Status\" IN ('Queued', 'Running')");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "ScanSchedules");

        migrationBuilder.DropIndex(
            name: "IX_ScanJobs_LibraryId_Active",
            table: "ScanJobs");
    }
}
