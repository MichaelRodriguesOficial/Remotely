using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Remotely.Server.Migrations.PostgreSql;

/// <summary>Histórico da aba Diagnóstico (testes de componentes).</summary>
public partial class Add_DeviceDiagnostics : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "DeviceDiagnosticRuns",
            columns: table => new
            {
                ID = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                DeviceID = table.Column<string>(type: "text", nullable: false),
                RequestedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                Tests = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                FinishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                Data = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DeviceDiagnosticRuns", x => x.ID);
                table.ForeignKey(
                    name: "FK_DeviceDiagnosticRuns_Devices_DeviceID",
                    column: x => x.DeviceID,
                    principalTable: "Devices",
                    principalColumn: "ID",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_DeviceDiagnosticRuns_DeviceID",
            table: "DeviceDiagnosticRuns",
            column: "DeviceID");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "DeviceDiagnosticRuns");
    }
}
