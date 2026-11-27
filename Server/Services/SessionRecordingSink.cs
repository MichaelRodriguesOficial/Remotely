using Remotely.Server.Models;

namespace Remotely.Server.Services;


/// <summary>
/// The service is responsible for storing session recordings.
/// </summary>
public interface ISessionRecordingSink
{
    /// <summary>
    /// Sink a live webm stream to persistent storage. 
    /// </summary>
    /// <param name="webmStream"></param>
    /// <param name="hubCallerContext"></param>
    /// <param name="session"></param>
    /// <returns></returns>
    Task SinkWebmStream(
        IAsyncEnumerable<byte[]> webmStream,
        RemoteControlSession session,
        Func<string, Task>? onFileCreated = null);
}


public class SessionRecordingSink : ISessionRecordingSink
{
    private readonly ILogger<SessionRecordingSink> _logger;

    public SessionRecordingSink(ILogger<SessionRecordingSink> logger)
    {
        _logger = logger;
    }

    public static string RecordingsDirectory
    {
        get
        {
            return Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "AppData",
                "recordings");
        }
    }

    /// <summary>Caminho relativo (ex.: 2026-11-27/arquivo.webm) para o arquivo dentro da pasta de gravações, ou nulo se sair dela.</summary>
    public static string? ResolveRecordingPath(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return null;
        }

        var root = Path.GetFullPath(RecordingsDirectory);
        var full = Path.GetFullPath(Path.Combine(root, relativePath));
        return full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? full : null;
    }

    /// <summary>Só letras, números, ponto, hífen e sublinhado (o nome vem do técnico).</summary>
    internal static string SafeFilePart(string? value)
    {
        var text = new string((value ?? string.Empty)
            .Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_')
            .ToArray())
            .Trim('.', '_');
        if (text.Length > 60)
        {
            text = text[..60];
        }
        return string.IsNullOrWhiteSpace(text) ? "tecnico" : text;
    }

    public async Task SinkWebmStream(IAsyncEnumerable<byte[]> webmStream, RemoteControlSession session, Func<string, Task>? onFileCreated = null)
    {
        long total = 0;
        var chunks = 0;
        var fileName = string.Empty;
        try
        {
            var day = $"{DateTime.Now:yyyy-MM-dd}";
            var targetDir = Path.Combine(RecordingsDirectory, day);
            _ = Directory.CreateDirectory(targetDir);

            var viewerName = SafeFilePart(string.IsNullOrWhiteSpace(session.RequesterName) ? session.RequesterUserName : session.RequesterName);
            var machine = SafeFilePart(session.MachineName);
            fileName = $"{DateTime.Now:yyyyMMdd_HHmmssfff}_{machine}_{viewerName}.webm";

            using var fs = new FileStream(Path.Combine(targetDir, fileName), FileMode.CreateNew);
            if (onFileCreated is not null)
            {
                try
                {
                    await onFileCreated($"{day}/{fileName}");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not link recording {file} to the session log.", fileName);
                }
            }

            await foreach (var chunk in webmStream)
            {
                await fs.WriteAsync(chunk);
                total += chunk.Length;
                chunks++;
            }

            _logger.LogInformation("Session recording saved: {file} ({chunks} chunks, {bytes} bytes).", fileName, chunks, total);
        }
        catch (OperationCanceledException)
        {
            // A aba do visualizador fechou: é o fim normal da gravação.
            _logger.LogInformation("Session recording ended with the viewer: {file} ({chunks} chunks, {bytes} bytes).", fileName, chunks, total);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while sinking webm stream ({chunks} chunks, {bytes} bytes).", chunks, total);
        }
    }
}
