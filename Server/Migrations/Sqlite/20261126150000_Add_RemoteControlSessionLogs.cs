using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Remotely.Server.Migrations.Sqlite;

/// <summary>Histórico das sessões de controle remoto.</summary>
public partial class Add_RemoteControlSessionLogs : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "RemoteControlSessionLogs",
            columns: table => new
            {
                ID = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                DeviceID = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                DeviceName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                EndReason = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                EndedAt = table.Column<string>(type: "TEXT", nullable: true),
                Mode = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                OrganizationID = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                RequesterName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                RequesterUserName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                StartedAt = table.Column<string>(type: "TEXT", nullable: false),
                ViewOnly = table.Column<bool>(type: "INTEGER", nullable: false),
                ViewerIp = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_RemoteControlSessionLogs", x => x.ID);
            });

        migrationBuilder.CreateIndex(
            name: "IX_RemoteControlSessionLogs_OrganizationID_StartedAt",
            table: "RemoteControlSessionLogs",
            columns: new[] { "OrganizationID", "StartedAt" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "RemoteControlSessionLogs");
    }
}
