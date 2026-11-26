using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Remotely.Server.Migrations.Sqlite;

/// <summary>Aceite do usuário e aviso de sessão por grupo de dispositivos.</summary>
public partial class Add_DeviceGroupRemoteSettings : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "RemoteControlConsent",
            table: "DeviceGroups",
            type: "TEXT",
            maxLength: 20,
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "NotifyRemoteSessions",
            table: "DeviceGroups",
            type: "INTEGER",
            nullable: false,
            defaultValue: false);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "RemoteControlConsent",
            table: "DeviceGroups");

        migrationBuilder.DropColumn(
            name: "NotifyRemoteSessions",
            table: "DeviceGroups");
    }
}
