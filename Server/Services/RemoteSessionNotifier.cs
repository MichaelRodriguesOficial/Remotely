using System.Net;
using System.Text.Json;
using Remotely.Server.Models;

namespace Remotely.Server.Services;

/// <summary>Avisa (e-mail e/ou webhook) quando uma sessão começa num computador de grupo com aviso ligado.</summary>
public interface IRemoteSessionNotifier
{
    Task NotifySessionStarted(RemoteControlSession session, string? requesterName, string? viewerIp);
}

public class RemoteSessionNotifier : IRemoteSessionNotifier
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<RemoteSessionNotifier> _logger;

    public RemoteSessionNotifier(
        IServiceScopeFactory scopeFactory,
        IHttpClientFactory httpClientFactory,
        ILogger<RemoteSessionNotifier> logger)
    {
        _scopeFactory = scopeFactory;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task NotifySessionStarted(RemoteControlSession session, string? requesterName, string? viewerIp)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(session.DeviceGroupId))
            {
                return;
            }

            using var scope = _scopeFactory.CreateScope();
            var dataService = scope.ServiceProvider.GetRequiredService<IDataService>();
            var groupResult = await dataService.GetDeviceGroup(session.DeviceGroupId);
            if (!groupResult.IsSuccess || !groupResult.Value.NotifyRemoteSessions)
            {
                return;
            }

            var settings = await dataService.GetSettings();
            var device = await dataService.GetDevice(session.DeviceId);
            var deviceName = device.IsSuccess ? device.Value.DeviceName ?? session.DeviceId : session.MachineName;
            var technician = string.IsNullOrWhiteSpace(requesterName) ? session.RequesterUserName : requesterName;
            var started = DateTimeOffset.Now;

            var webhook = settings.RemoteControlNotifyWebhook?.Trim();
            if (Uri.TryCreate(webhook, UriKind.Absolute, out var url) && url.Scheme is "https" or "http")
            {
                var payload = new
                {
                    @event = "remote_session_started",
                    device = deviceName,
                    deviceId = session.DeviceId,
                    group = groupResult.Value.Name,
                    technician,
                    login = session.RequesterUserName,
                    viewerIp,
                    mode = session.ViewOnly ? "view" : "control",
                    startedAt = started
                };
                using var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(10);
                // Corpo com tamanho definido (sem "chunked"): alguns receptores de webhook não aceitam.
                using var content = new StringContent(JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json");
                using var response = await client.PostAsync(url, content);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Remote session webhook returned {status}.", (int)response.StatusCode);
                }
            }

            var recipients = (settings.RemoteControlNotifyEmails ?? string.Empty)
                .Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(x => x.Contains('@'))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (recipients.Count > 0)
            {
                var sender = scope.ServiceProvider.GetRequiredService<IEmailSenderEx>();
                var subject = $"Acesso remoto: {deviceName} ({groupResult.Value.Name})";
                var html = $"""
                    <p><strong>{Enc(technician)}</strong> abriu uma sessão de controle remoto em <strong>{Enc(deviceName)}</strong>.</p>
                    <ul>
                      <li>Grupo: {Enc(groupResult.Value.Name)}</li>
                      <li>Login: {Enc(session.RequesterUserName)}</li>
                      <li>Modo: {(session.ViewOnly ? "só visualizar" : "controle")}</li>
                      <li>IP do técnico: {Enc(viewerIp)}</li>
                      <li>Início: {started:dd/MM/yyyy HH:mm}</li>
                    </ul>
                    <p>O histórico completo fica em Sessões remotas.</p>
                    """;
                foreach (var recipient in recipients)
                {
                    await sender.SendEmailAsync(recipient, subject, html, session.OrganizationId);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while sending the remote session notification.");
        }
    }

    private static string Enc(string? value) => WebUtility.HtmlEncode(value ?? "—");
}
