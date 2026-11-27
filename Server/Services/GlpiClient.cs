using Remotely.Server.Models;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Remotely.Server.Services;

/// <summary>
/// Registra no GLPI (API REST) o que foi feito numa sessão de controle remoto: acompanhamento num
/// chamado existente ou um chamado novo, ligado ao computador quando ele existe no GLPI com o mesmo nome.
/// </summary>
public interface IGlpiClient
{
    bool IsConfigured(SettingsModel settings);

    /// <summary>Acompanhamento no chamado. Retorna o número do chamado.</summary>
    Task<Result<string>> AddFollowup(SettingsModel settings, string ticketId, string content);

    /// <summary>Abre um chamado. Retorna o número.</summary>
    Task<Result<string>> CreateTicket(SettingsModel settings, string title, string content, string? computerName);
}

public class GlpiClient : IGlpiClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<GlpiClient> _logger;

    public GlpiClient(IHttpClientFactory httpClientFactory, ILogger<GlpiClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public bool IsConfigured(SettingsModel settings) =>
        Uri.TryCreate(settings.GlpiUrl, UriKind.Absolute, out var uri) &&
        uri.Scheme is "https" or "http" &&
        !string.IsNullOrWhiteSpace(settings.GlpiUserToken);

    public async Task<Result<string>> AddFollowup(SettingsModel settings, string ticketId, string content)
    {
        if (!int.TryParse(ticketId.Trim().TrimStart('#'), out var id) || id <= 0)
        {
            return Result.Fail<string>("Número do chamado inválido.");
        }

        return await WithSession(settings, async (client, api) =>
        {
            var body = new JsonObject
            {
                ["input"] = new JsonObject
                {
                    ["itemtype"] = "Ticket",
                    ["items_id"] = id,
                    ["content"] = ToHtml(content)
                }
            };
            using var response = await client.PostAsync($"{api}/ITILFollowup", Json(body));
            if (!response.IsSuccessStatusCode)
            {
                return Result.Fail<string>(await ErrorMessage(response, "Não foi possível adicionar o acompanhamento."));
            }
            return Result.Ok($"{id}");
        });
    }

    public async Task<Result<string>> CreateTicket(SettingsModel settings, string title, string content, string? computerName)
    {
        return await WithSession(settings, async (client, api) =>
        {
            var body = new JsonObject
            {
                ["input"] = new JsonObject
                {
                    ["name"] = title,
                    ["content"] = ToHtml(content),
                    ["type"] = 1
                }
            };
            using var response = await client.PostAsync($"{api}/Ticket", Json(body));
            if (!response.IsSuccessStatusCode)
            {
                return Result.Fail<string>(await ErrorMessage(response, "Não foi possível abrir o chamado."));
            }

            var created = JsonNode.Parse(await response.Content.ReadAsStringAsync());
            var ticketId = created?["id"]?.GetValue<int>() ?? 0;
            if (ticketId <= 0)
            {
                return Result.Fail<string>("O GLPI não devolveu o número do chamado.");
            }

            // Liga o computador ao chamado quando há um com o mesmo nome (melhor esforço).
            if (!string.IsNullOrWhiteSpace(computerName))
            {
                try
                {
                    var search = $"{api}/search/Computer?criteria[0][field]=1&criteria[0][searchtype]=equals" +
                        $"&criteria[0][value]={Uri.EscapeDataString(computerName)}&forcedisplay[0]=2&range=0-1";
                    using var found = await client.GetAsync(search);
                    if (found.IsSuccessStatusCode)
                    {
                        var data = JsonNode.Parse(await found.Content.ReadAsStringAsync())?["data"]?.AsArray();
                        var computerId = data?.FirstOrDefault()?["2"]?.ToString();
                        if (int.TryParse(computerId, out var computer))
                        {
                            var link = new JsonObject
                            {
                                ["input"] = new JsonObject
                                {
                                    ["tickets_id"] = ticketId,
                                    ["itemtype"] = "Computer",
                                    ["items_id"] = computer
                                }
                            };
                            using var _ = await client.PostAsync($"{api}/Item_Ticket", Json(link));
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogInformation(ex, "Could not link computer {computer} to GLPI ticket {ticket}.", computerName, ticketId);
                }
            }

            return Result.Ok($"{ticketId}");
        });
    }

    private async Task<Result<string>> WithSession(SettingsModel settings, Func<HttpClient, string, Task<Result<string>>> action)
    {
        if (!IsConfigured(settings))
        {
            return Result.Fail<string>("O GLPI não está configurado (Configuração → GLPI).");
        }

        var api = $"{settings.GlpiUrl.TrimEnd('/')}/apirest.php";
        using var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(20);
        if (!string.IsNullOrWhiteSpace(settings.GlpiAppToken))
        {
            client.DefaultRequestHeaders.Add("App-Token", settings.GlpiAppToken);
        }

        try
        {
            using var init = new HttpRequestMessage(HttpMethod.Get, $"{api}/initSession");
            init.Headers.Authorization = new AuthenticationHeaderValue("user_token", settings.GlpiUserToken);
            using var initResponse = await client.SendAsync(init);
            if (!initResponse.IsSuccessStatusCode)
            {
                return Result.Fail<string>(await ErrorMessage(initResponse, "O GLPI recusou o token. Confira o App-Token e o token do usuário."));
            }

            var sessionToken = JsonNode.Parse(await initResponse.Content.ReadAsStringAsync())?["session_token"]?.ToString();
            if (string.IsNullOrWhiteSpace(sessionToken))
            {
                return Result.Fail<string>("O GLPI não abriu a sessão da API.");
            }

            client.DefaultRequestHeaders.Add("Session-Token", sessionToken);
            try
            {
                return await action(client, api);
            }
            finally
            {
                try
                {
                    using var _ = await client.GetAsync($"{api}/killSession");
                }
                catch
                {
                    // Sessão expira sozinha.
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error while talking to GLPI at {url}.", settings.GlpiUrl);
            return Result.Fail<string>("Sem conexão com o GLPI.");
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static StringContent Json(JsonNode body) =>
        new(body.ToJsonString(JsonOptions), Encoding.UTF8, "application/json");

    /// <summary>Texto simples para o editor do GLPI (parágrafos e quebras de linha, tudo escapado).</summary>
    internal static string ToHtml(string text) =>
        string.Join("", (text ?? string.Empty)
            .Replace("\r\n", "\n")
            .Split("\n\n")
            .Select(p => $"<p>{Escape(p).Replace("\n", "<br>")}</p>"));

    private static string Escape(string text) => text
        .Replace("&", "&amp;")
        .Replace("<", "&lt;")
        .Replace(">", "&gt;")
        .Replace("\"", "&quot;");

    private static async Task<string> ErrorMessage(HttpResponseMessage response, string fallback)
    {
        try
        {
            var text = await response.Content.ReadAsStringAsync();
            // O GLPI responde ["ERROR_...", "mensagem"].
            var node = JsonNode.Parse(text);
            if (node is JsonArray array && array.Count > 1)
            {
                return $"{fallback} ({array[1]})";
            }
        }
        catch (JsonException)
        {
            // Resposta sem JSON.
        }
        return $"{fallback} (HTTP {(int)response.StatusCode})";
    }
}
