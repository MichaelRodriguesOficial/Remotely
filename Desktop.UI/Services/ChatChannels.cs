using System.Net.Http;
using System.Threading;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Remotely.Desktop.Shared.Services;
using Remotely.Desktop.UI.ViewModels;
using Remotely.Shared;
using Remotely.Shared.Models;
using Remotely.Shared.Models.Dtos;

namespace Remotely.Desktop.UI.Services;

/// <summary>Chat da sessão: pelo canal da tela, anexos com os bytes junto.</summary>
public class SessionChatChannel : IChatChannel
{
    private readonly ISessionChat _sessionChat;
    private readonly string _myName;

    public SessionChatChannel(ISessionChat sessionChat, string myName)
    {
        _sessionChat = sessionChat;
        _myName = myName;
    }

    public bool IsSession => true;

    public Task<bool> Send(ChatMessageDto message) => _sessionChat.Send(message);

    public Task SendTyping() => _sessionChat.SendTyping(_myName);

    public Task SendRead(string messageId) => _sessionChat.SendRead(messageId);

    public Task<byte[]?> LoadAttachment(ChatAttachmentDto attachment) => Task.FromResult(attachment.Data);
}

/// <summary>
/// Chat avulso (pelo card do painel): mensagens pelo agente (pipe) e anexos guardados no servidor,
/// com o token temporário que vem nas mensagens do técnico.
/// </summary>
public class PipeChatChannel : IChatChannel
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(5) };
    private readonly StreamWriter _writer;
    private readonly string _host;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public PipeChatChannel(StreamWriter writer, string host, ILogger logger)
    {
        _writer = writer;
        _host = (host ?? string.Empty).TrimEnd('/');
        _logger = logger;
    }

    /// <summary>Atualizado a cada mensagem do técnico.</summary>
    public string? FileToken { get; set; }

    public bool IsSession => false;

    public async Task<bool> Send(ChatMessageDto message)
    {
        var chat = new ChatMessage(string.Empty, message.Text)
        {
            Id = message.Id,
            Kind = "message"
        };

        foreach (var attachment in message.Attachments)
        {
            var fileId = await Upload(attachment);
            if (fileId is null)
            {
                return false;
            }
            chat.Attachments.Add(new ChatAttachmentDto
            {
                Id = attachment.Id,
                FileName = attachment.FileName,
                ContentType = attachment.ContentType,
                Size = attachment.Size,
                FileId = fileId
            });
        }

        return await Write(chat);
    }

    public Task SendTyping() => Write(new ChatMessage { Kind = "typing" });

    public Task SendRead(string messageId) => Write(new ChatMessage { Kind = "read", Message = messageId });

    public async Task<byte[]?> LoadAttachment(ChatAttachmentDto attachment)
    {
        if (attachment.Data is { Length: > 0 } data)
        {
            return data;
        }
        if (string.IsNullOrWhiteSpace(attachment.FileId) || string.IsNullOrWhiteSpace(FileToken))
        {
            return null;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{_host}/API/FileSharing/{Uri.EscapeDataString(attachment.FileId)}");
            request.Headers.Add(AppConstants.ExpiringTokenHeaderName, FileToken);
            using var response = await _http.SendAsync(request);
            return response.IsSuccessStatusCode ? await response.Content.ReadAsByteArrayAsync() : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not download chat attachment.");
            return null;
        }
    }

    private async Task<string?> Upload(ChatAttachmentDto attachment)
    {
        if (attachment.Data is not { Length: > 0 } data || string.IsNullOrWhiteSpace(FileToken))
        {
            return null;
        }

        try
        {
            using var content = new MultipartFormDataContent();
            var file = new ByteArrayContent(data);
            file.Headers.ContentType = MediaTypeHeaderValue.Parse(attachment.ContentType);
            content.Add(file, "file", attachment.FileName);
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{_host}/API/FileSharing") { Content = content };
            request.Headers.Add(AppConstants.ExpiringTokenHeaderName, FileToken);
            using var response = await _http.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }
            var ids = JsonSerializer.Deserialize<string[]>(await response.Content.ReadAsStringAsync());
            return ids?.FirstOrDefault();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not upload chat attachment.");
            return null;
        }
    }

    private async Task<bool> Write(ChatMessage message)
    {
        await _writeLock.WaitAsync();
        try
        {
            await _writer.WriteLineAsync(JsonSerializer.Serialize(message));
            await _writer.FlushAsync();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not write chat message to the agent.");
            return false;
        }
        finally
        {
            _writeLock.Release();
        }
    }
}
