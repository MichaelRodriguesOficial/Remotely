using Remotely.Shared.Enums;

namespace Remotely.Desktop.Shared.Abstractions;

public interface IRemoteControlAccessService
{
    bool IsPromptOpen { get; }

    /// <param name="autoAcceptSeconds">Se maior que zero, aceita sozinho quando o usuário não responde nesse tempo.</param>
    Task<PromptForAccessResult> PromptForAccess(string requesterName, string organizationName, int autoAcceptSeconds = 0);
}
