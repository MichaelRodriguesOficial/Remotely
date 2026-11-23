using Remotely.Shared.Models.Inventory;
using System.Threading;
using System.Threading.Tasks;

namespace Remotely.Agent.Interfaces;

/// <summary>
/// Coleta o inventário de hardware e software do computador (no estilo do GLPI Inventory).
/// </summary>
public interface IInventoryCollector
{
    Task<DeviceInventory> Collect(CancellationToken cancellationToken);
}
