using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Remotely.Server.Migrations.SqlServer;

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
                ID = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                DeviceID = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                DeviceName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                EndReason = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                EndedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                Mode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                OrganizationID = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                RequesterName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                RequesterUserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                ViewOnly = table.Column<bool>(type: "bit", nullable: false),
                ViewerIp = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true)
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
