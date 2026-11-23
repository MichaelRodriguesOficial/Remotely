using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Remotely.Server.Migrations.Sqlite;

/// <summary>
/// Inventário de hardware e software dos dispositivos, número de inventário,
/// localização e nome/sobrenome do usuário logado.
/// </summary>
public partial class Add_DeviceInventory : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "AssetTag",
            table: "Devices",
            type: "TEXT",
            maxLength: 100,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "CurrentUserGivenName",
            table: "Devices",
            type: "TEXT",
            maxLength: 100,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "CurrentUserSurname",
            table: "Devices",
            type: "TEXT",
            maxLength: 100,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "DeviceType",
            table: "Devices",
            type: "TEXT",
            maxLength: 50,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Location",
            table: "Devices",
            type: "TEXT",
            maxLength: 200,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Manufacturer",
            table: "Devices",
            type: "TEXT",
            maxLength: 200,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Model",
            table: "Devices",
            type: "TEXT",
            maxLength: 200,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "SerialNumber",
            table: "Devices",
            type: "TEXT",
            maxLength: 200,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "InventoryUpdatedAt",
            table: "Devices",
            type: "TEXT",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "DeviceInventories",
            columns: table => new
            {
                DeviceID = table.Column<string>(type: "TEXT", nullable: false),
                CollectedAt = table.Column<string>(type: "TEXT", nullable: false),
                Data = table.Column<string>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DeviceInventories", x => x.DeviceID);
                table.ForeignKey(
                    name: "FK_DeviceInventories_Devices_DeviceID",
                    column: x => x.DeviceID,
                    principalTable: "Devices",
                    principalColumn: "ID",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Devices_AssetTag",
            table: "Devices",
            column: "AssetTag");

        migrationBuilder.CreateIndex(
            name: "IX_Devices_SerialNumber",
            table: "Devices",
            column: "SerialNumber");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "DeviceInventories");

        migrationBuilder.DropIndex(
            name: "IX_Devices_AssetTag",
            table: "Devices");

        migrationBuilder.DropIndex(
            name: "IX_Devices_SerialNumber",
            table: "Devices");

        migrationBuilder.DropColumn(
            name: "AssetTag",
            table: "Devices");

        migrationBuilder.DropColumn(
            name: "CurrentUserGivenName",
            table: "Devices");

        migrationBuilder.DropColumn(
            name: "CurrentUserSurname",
            table: "Devices");

        migrationBuilder.DropColumn(
            name: "DeviceType",
            table: "Devices");

        migrationBuilder.DropColumn(
            name: "Location",
            table: "Devices");

        migrationBuilder.DropColumn(
            name: "Manufacturer",
            table: "Devices");

        migrationBuilder.DropColumn(
            name: "Model",
            table: "Devices");

        migrationBuilder.DropColumn(
            name: "SerialNumber",
            table: "Devices");

        migrationBuilder.DropColumn(
            name: "InventoryUpdatedAt",
            table: "Devices");
    }
}
