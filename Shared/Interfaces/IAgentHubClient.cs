using Remotely.Shared.Enums;

namespace Remotely.Shared.Interfaces;
public interface IAgentHubClient
{
    Task ChangeWindowsSession(
        string viewerConnectionId,
        string sessionId,
        string accessKey,
        string userConnectionId,
        string requesterName,
        string orgName,
        string orgId,
        int targetSessionId);

    Task SendChatMessage(
        string senderName, 
        string message, 
        string orgName, 
        string orgId, 
        bool disconnected, 
        string senderConnectionId);

    Task InvokeCtrlAltDel();

    Task DeleteLogs();

    Task ExecuteCommand(
        ScriptingShell shell, 
        string command, 
        string authToken, 
        string senderUsername, 
        string senderConnectionId);

    Task ExecuteCommandFromApi(ScriptingShell shell,
            string authToken,
            string requestID,
            string command,
            string senderUsername);

    Task GetLogs(string senderConnectionId);

    Task GetPowerShellCompletions(
        string inputText, 
        int currentIndex, 
        CompletionIntent intent, 
        bool? forward, 
        string senderConnectionId);

    Task ReinstallAgent();

    Task UninstallAgent();

    Task RemoteControl(
        Guid sessionId, 
        string accessKey, 
        string userConnectionId, 
        string requesterName, 
        string orgName, 
        string orgId);

    Task RestartScreenCaster(
        string[] viewerIds, 
        string sessionId, 
        string accessKey, 
        string userConnectionId, 
        string requesterName, 
        string orgName, 
        string orgId);

    Task RunScript(
        Guid savedScriptId, 
        int scriptRunId, 
        string initiator, 
        ScriptInputType scriptInputType, 
        string authToken);

    Task TransferFileFromBrowserToAgent(
        string transferId, 
        string[] fileIds, 
        string requesterId, 
        string expiringToken);

    /// <summary>
    /// Baixa os arquivos enviados pelo painel e salva no destino escolhido
    /// (ver <see cref="Remotely.Shared.Models.FileTransferDestinations"/>). Responde com FileTransferResult.
    /// </summary>
    Task ReceiveFiles(
        string transferId,
        string[] fileIds,
        string requesterId,
        string expiringToken,
        string destination);

    Task TriggerHeartbeat();

    /// <summary>Coleta o inventário de hardware e software e envia ao servidor.</summary>
    Task RequestInventory();

    /// <summary>Executa os testes da aba Diagnóstico e envia o resultado com ReportDiagnostics.</summary>
    Task RunDiagnostics(string runId, string[] tests);

    /// <summary>Executa um reparo da lista fechada <c>DiagnosticRepairs</c> e envia o resultado com ReportDiagnostics.</summary>
    Task RunRepair(string runId, string repairId, string? target);

    /// <summary>Interrompe o diagnóstico/reparo em andamento e desfaz um teste de memória agendado.</summary>
    Task CancelDiagnostics();

    /// <summary>Reinicia ("restart") ou desliga ("shutdown") o computador com aviso ao usuário.</summary>
    Task PowerAction(string action, int delaySeconds);

    Task WakeDevice(string macAddress);
}
