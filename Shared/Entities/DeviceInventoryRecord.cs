using System.ComponentModel.DataAnnotations;

namespace Remotely.Shared.Entities;

/// <summary>
/// Inventário completo do dispositivo (JSON), separado da tabela Devices para
/// não pesar nas listagens e nos heartbeats.
/// </summary>
public class DeviceInventoryRecord
{
    [Key]
    public string DeviceID { get; set; } = null!;

    public DateTimeOffset CollectedAt { get; set; }

    /// <summary>JSON de <see cref="Remotely.Shared.Models.Inventory.DeviceInventory"/>.</summary>
    public string Data { get; set; } = string.Empty;
}
