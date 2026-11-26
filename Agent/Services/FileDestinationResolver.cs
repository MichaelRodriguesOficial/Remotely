using Remotely.Desktop.Native.Windows;
using Remotely.Shared.Models;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Remotely.Agent.Services;

/// <summary>
/// Descobre a pasta real de cada destino do "Enviar arquivo". O agente roda como SYSTEM/root,
/// então "Downloads" e "Área de Trabalho" precisam apontar para o usuário que está logado.
/// </summary>
internal static class FileDestinationResolver
{
    private static readonly Guid FolderDownloads = new("374DE290-123F-4565-9164-39C4925E467B");
    private static readonly Guid FolderDesktop = new("B4BFCC3A-DB2C-424C-B029-7FE99A87C641");

    /// <summary>Pasta de destino resolvida.</summary>
    /// <param name="Folder">Caminho absoluto, já criado.</param>
    /// <param name="LinuxOwner">No Linux, usuário que deve ser dono dos arquivos salvos.</param>
    /// <param name="Note">Observação para o técnico (ex.: ninguém logado, usou a pasta pública).</param>
    public sealed record Resolved(string Folder, string? LinuxOwner, string? Note);

    public static Resolved Resolve(string kind, string? customPath)
    {
        Resolved result;
        if (kind == FileTransferDestinations.Temp)
        {
            result = new(Path.Combine(Path.GetTempPath(), "RemotelySharedFiles"), null, null);
        }
        else if (kind == FileTransferDestinations.PathPrefix)
        {
            result = new(Path.GetFullPath(customPath!), null, null);
        }
        else if (OperatingSystem.IsWindows())
        {
            result = ResolveWindows(kind);
        }
        else
        {
            result = ResolveUnix(kind);
        }

        Directory.CreateDirectory(result.Folder);
        return result;
    }

    /// <summary>Nome livre na pasta: "arquivo.pdf", depois "arquivo (2).pdf", "arquivo (3).pdf"...</summary>
    public static string GetAvailablePath(string folder, string fileName)
    {
        var candidate = Path.Combine(folder, fileName);
        if (!File.Exists(candidate))
        {
            return candidate;
        }

        var name = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        for (var i = 2; i < 1000; i++)
        {
            candidate = Path.Combine(folder, $"{name} ({i}){extension}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(folder, $"{name} ({Guid.NewGuid():N}){extension}");
    }

    /// <summary>Remove caracteres proibidos e qualquer pedaço de pasta do nome vindo do servidor.</summary>
    public static string SanitizeFileName(string? fileName)
    {
        var name = (fileName ?? string.Empty).Trim().Trim('"');
        name = name.Replace('\\', '/');
        name = name[(name.LastIndexOf('/') + 1)..];

        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        name = new string(name.Where(c => !invalid.Contains(c) && !char.IsControl(c)).ToArray()).Trim().TrimEnd('.');

        if (name.Length == 0 || name is "." or "..")
        {
            return Path.GetRandomFileName();
        }

        return name.Length > 180 ? name[..180] : name;
    }

    [SupportedOSPlatform("windows")]
    private static Resolved ResolveWindows(string kind)
    {
        var publicRoot = Environment.GetEnvironmentVariable("PUBLIC") is { Length: > 0 } pub
            ? pub
            : Path.Combine(Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\", "Users", "Public");

        if (kind == FileTransferDestinations.PublicDesktop)
        {
            return new(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory) is { Length: > 0 } common
                ? common
                : Path.Combine(publicRoot, "Desktop"), null, null);
        }

        var folderId = kind == FileTransferDestinations.Desktop ? FolderDesktop : FolderDownloads;
        var publicFallback = Path.Combine(publicRoot, kind == FileTransferDestinations.Desktop ? "Desktop" : "Downloads");

        var session = Win32Interop.GetActiveSessions()
            .Where(x => !string.IsNullOrWhiteSpace(x.Username))
            .OrderBy(x => x.Type == Remotely.Shared.Models.WindowsSessionType.Console ? 0 : 1)
            .FirstOrDefault();

        if (session is null)
        {
            return new(publicFallback, null, "Nenhum usuário logado; o arquivo foi para a pasta pública.");
        }

        if (TryGetKnownFolderForSession(session.Id, folderId, out var knownFolder))
        {
            return new(knownFolder, null, null);
        }

        // Sem token (agente rodando fora do SYSTEM, por exemplo): tenta o perfil pelo nome.
        var profiles = Path.GetDirectoryName(publicRoot) ?? @"C:\Users";
        var user = session.Username.Contains('\\') ? session.Username[(session.Username.LastIndexOf('\\') + 1)..] : session.Username;
        var profile = Path.Combine(profiles, user);
        if (Directory.Exists(profile))
        {
            return new(Path.Combine(profile, kind == FileTransferDestinations.Desktop ? "Desktop" : "Downloads"), null, null);
        }

        return new(publicFallback, null, $"Não achei o perfil de {session.Username}; o arquivo foi para a pasta pública.");
    }

    [SupportedOSPlatform("windows")]
    private static bool TryGetKnownFolderForSession(uint sessionId, Guid folderId, out string path)
    {
        path = string.Empty;
        nint token = nint.Zero;
        nint buffer = nint.Zero;
        try
        {
            if (!WTSQueryUserToken(sessionId, out token) || token == nint.Zero)
            {
                return false;
            }

            var hr = SHGetKnownFolderPath(folderId, 0, token, out buffer);
            if (hr != 0 || buffer == nint.Zero)
            {
                return false;
            }

            path = Marshal.PtrToStringUni(buffer) ?? string.Empty;
            return path.Length > 0;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (buffer != nint.Zero)
            {
                Marshal.FreeCoTaskMem(buffer);
            }
            if (token != nint.Zero)
            {
                CloseHandle(token);
            }
        }
    }

    private static Resolved ResolveUnix(string kind)
    {
        var user = GetLoggedUnixUser();
        if (user is null)
        {
            return new(Path.Combine(Path.GetTempPath(), "RemotelySharedFiles"), null,
                "Nenhum usuário logado; o arquivo foi para a pasta temporária do agente.");
        }

        var home = GetUnixHome(user) ?? Path.Combine("/home", user);
        var xdgName = kind == FileTransferDestinations.Desktop ? "DESKTOP" : "DOWNLOAD";
        var folder = RunAsUser(user, "xdg-user-dir", xdgName);

        if (string.IsNullOrWhiteSpace(folder) || folder.TrimEnd('/') == home.TrimEnd('/') || !folder.StartsWith('/'))
        {
            var fallback = kind == FileTransferDestinations.Desktop ? new[] { "Desktop", "Área de Trabalho" } : new[] { "Downloads", "Transferências" };
            folder = fallback.Select(x => Path.Combine(home, x)).FirstOrDefault(Directory.Exists) ?? Path.Combine(home, fallback[0]);
        }

        return new(folder.Trim(), user, null);
    }

    private static string? GetLoggedUnixUser()
    {
        var output = RunProcess("who", Array.Empty<string>());
        var users = output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => (User: line.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(), Line: line))
            .Where(x => !string.IsNullOrWhiteSpace(x.User) && x.User != "root")
            .ToList();

        // Prefere quem está na sessão gráfica (":0", "seat0", "tty7"...).
        return users.FirstOrDefault(x => x.Line.Contains("(:") || x.Line.Contains("seat") || x.Line.Contains("tty7")).User
            ?? users.FirstOrDefault().User;
    }

    private static string? GetUnixHome(string user)
    {
        var line = RunProcess("getent", ["passwd", user]).Split('\n').FirstOrDefault() ?? string.Empty;
        var parts = line.Split(':');
        return parts.Length >= 6 && parts[5].StartsWith('/') ? parts[5] : null;
    }

    private static string RunAsUser(string user, string command, params string[] args)
    {
        return RunProcess("runuser", ["-u", user, "--", command, .. args]).Trim();
    }

    /// <summary>Entrega o arquivo salvo como root ao usuário logado.</summary>
    public static void ChownToUser(string path, string user)
    {
        _ = RunProcess("chown", [$"{user}:", path]);
    }

    private static string RunProcess(string fileName, string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo(fileName)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (var arg in args)
            {
                psi.ArgumentList.Add(arg);
            }

            using var process = Process.Start(psi);
            if (process is null)
            {
                return string.Empty;
            }

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(10_000);
            return output;
        }
        catch
        {
            return string.Empty;
        }
    }

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSQueryUserToken(uint sessionId, out nint token);

    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid rfid, uint flags, nint token, out nint path);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(nint handle);
}
