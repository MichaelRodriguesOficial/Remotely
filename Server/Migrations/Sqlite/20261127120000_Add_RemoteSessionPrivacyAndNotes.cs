using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Remotely.Server.Migrations.Sqlite;

/// <summary>Tela preta e gravação por grupo; anotação, gravação e chamado no histórico de sessões.</summary>
public partial class Add_RemoteSessionPrivacyAndNotes : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "PrivacyScreen",
            table: "DeviceGroups",
            type: "TEXT",
            maxLength: 20,
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "RecordSessions",
            table: "DeviceGroups",
            type: "INTEGER",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<string>(
            name: "Note",
            table: "RemoteControlSessionLogs",
            type: "TEXT",
            maxLength: 4000,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "RecordingFile",
            table: "RemoteControlSessionLogs",
            type: "TEXT",
            maxLength: 400,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "TicketId",
            table: "RemoteControlSessionLogs",
            type: "TEXT",
            maxLength: 50,
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "PrivacyScreen",
            table: "DeviceGroups");

        migrationBuilder.DropColumn(
            name: "RecordSessions",
            table: "DeviceGroups");

        migrationBuilder.DropColumn(
            name: "Note",
            table: "RemoteControlSessionLogs");

        migrationBuilder.DropColumn(
            name: "RecordingFile",
            table: "RemoteControlSessionLogs");

        migrationBuilder.DropColumn(
            name: "TicketId",
            table: "RemoteControlSessionLogs");
    }
}
