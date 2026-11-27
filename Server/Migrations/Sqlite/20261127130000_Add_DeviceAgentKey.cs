using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Remotely.Server.Migrations.Sqlite;

/// <summary>Chave do agente: o servidor guarda só o hash da chave de cada computador.</summary>
public partial class Add_DeviceAgentKey : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "AgentKeyHash",
            table: "Devices",
            type: "TEXT",
            maxLength: 100,
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "AgentKeyHash",
            table: "Devices");
    }
}
