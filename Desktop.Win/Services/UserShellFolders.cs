using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Remotely.Desktop.Native.Windows;

namespace Remotely.Desktop.Win.Services;

/// <summary>
/// Pastas do usuário logado e a pasta "sob o cursor" na tela. O app de controle remoto roda como SYSTEM
/// na sessão do usuário (acesso pelo painel): as pastas comuns (Área de Trabalho, Downloads) são as do
/// usuário logado, lidas pelo token dele, e não as do SYSTEM.
/// </summary>
[SupportedOSPlatform("windows")]
public static class UserShellFolders
{
    public static readonly Guid Desktop = new("B4BFCC3A-DB2C-424C-B029-7FE99A87C641");
    public static readonly Guid Downloads = new("374DE290-123F-4565-9164-39C4925E467B");
    public static readonly Guid Documents = new("FDD39AD0-238F-46AF-ADB4-6C85480369C7");
    public static readonly Guid PublicDesktop = new("C4AA340D-F20F-4863-AFEF-F87EF2E6BA25");

    /// <summary>Caminho da pasta conhecida do usuário logado (ou do próprio processo, no modo assistido).</summary>
    public static string? GetKnownFolder(Guid folderId)
    {
        var token = GetUserToken();
        try
        {
            if (SHGetKnownFolderPath(folderId, 0, token, out var pathPtr) == 0)
            {
                try
                {
                    var path = Marshal.PtrToStringUni(pathPtr);
                    if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
                    {
                        return path;
                    }
                }
                finally
                {
                    Marshal.FreeCoTaskMem(pathPtr);
                }
            }
        }
        finally
        {
            if (token != 0)
            {
                Kernel32.CloseHandle(token);
            }
        }

        return null;
    }

    /// <summary>
    /// Pasta para onde o arquivo foi solto: a Área de Trabalho, a pasta aberta no Explorador de Arquivos
    /// sob o ponto, ou null se o ponto não cai em nenhuma das duas.
    /// </summary>
    public static string? GetFolderAtPoint(int x, int y)
    {
        Win32Interop.SwitchToInputDesktop();
        return GetFolderOfWindow(WindowFromPoint(new POINT { X = x, Y = y }));
    }

    /// <summary>Pasta da janela em primeiro plano (para Ctrl+V de arquivos no visualizador).</summary>
    public static string? GetFolderOfForegroundWindow()
    {
        Win32Interop.SwitchToInputDesktop();
        return GetFolderOfWindow(GetForegroundWindow());
    }

    private static string? GetFolderOfWindow(nint hwnd)
    {
        if (hwnd == 0)
        {
            return null;
        }

        var root = GetAncestor(hwnd, 2 /* GA_ROOT */);
        var className = GetClass(root);
        if (className is "Progman" or "WorkerW")
        {
            return GetKnownFolder(Desktop);
        }

        if (className is "CabinetWClass" or "ExploreWClass")
        {
            return GetExplorerFolder(root);
        }

        return null;
    }

    private static string GetClass(nint hwnd)
    {
        var buffer = new StringBuilder(256);
        return GetClassName(hwnd, buffer, buffer.Capacity) > 0 ? buffer.ToString() : string.Empty;
    }

    private static string GetTitle(nint hwnd)
    {
        var buffer = new StringBuilder(512);
        return GetWindowText(hwnd, buffer, buffer.Capacity) > 0 ? buffer.ToString() : string.Empty;
    }

    /// <summary>
    /// Pasta aberta numa janela do Explorador (com abas no Windows 11, a aba cujo nome está no título).
    /// O objeto Shell.Application só responde no contexto do usuário, então a consulta roda num
    /// PowerShell oculto aberto como o usuário logado.
    /// </summary>
    private static string? GetExplorerFolder(nint hwnd)
    {
        var output = Path.Combine(Path.GetTempPath(), $"remotely-folder-{Guid.NewGuid():N}.txt");
        if (GetUserToken() is var probe && probe != 0)
        {
            Kernel32.CloseHandle(probe);
            // Como SYSTEM, grava num lugar que o usuário também pode escrever.
            output = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp", $"remotely-folder-{Guid.NewGuid():N}.txt");
        }

        var title = GetTitle(hwnd).Replace("'", "''");
        var script =
            "$ErrorActionPreference='SilentlyContinue';" +
            $"$h=[int64]{(long)hwnd};$t='{title}';" +
            "$w=@((New-Object -ComObject Shell.Application).Windows() | Where-Object { [int64]$_.HWND -eq $h });" +
            "$pick=$w | Where-Object { $t.StartsWith([string]$_.LocationName) } | Select-Object -First 1;" +
            "if(-not $pick){$pick=$w | Select-Object -First 1};" +
            "if($pick){[IO.File]::WriteAllText('" + output.Replace("'", "''") + "',[string]$pick.Document.Folder.Self.Path,[Text.Encoding]::UTF8)}";
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        var powershell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        var arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -EncodedCommand {encoded}";

        try
        {
            if (!RunAsLoggedOnUser(powershell, arguments, TimeSpan.FromSeconds(8)))
            {
                return null;
            }

            if (!File.Exists(output))
            {
                return null;
            }

            var path = File.ReadAllText(output).Trim().TrimStart('﻿');
            return Path.IsPathFullyQualified(path) && Directory.Exists(path) ? path : null;
        }
        catch
        {
            return null;
        }
        finally
        {
            try
            {
                File.Delete(output);
            }
            catch
            {
                // Arquivo temporário.
            }
        }
    }

    /// <summary>
    /// Janela "Abrir" (vários arquivos) como o usuário logado. Como SYSTEM, o seletor nunca roda com
    /// privilégio de SYSTEM: é um PowerShell aberto como o usuário que devolve os caminhos escolhidos.
    /// </summary>
    public static IReadOnlyList<string> PickFilesAsUser(string title)
    {
        var output = Path.Combine(Path.GetTempPath(), $"remotely-pick-{Guid.NewGuid():N}.txt");
        if (GetUserToken() is var probe && probe != 0)
        {
            Kernel32.CloseHandle(probe);
            output = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp", $"remotely-pick-{Guid.NewGuid():N}.txt");
        }

        var script =
            "$ErrorActionPreference='SilentlyContinue';" +
            "Add-Type -AssemblyName System.Windows.Forms;" +
            "$owner=New-Object System.Windows.Forms.Form -Property @{TopMost=$true;ShowInTaskbar=$false;WindowState='Minimized'};" +
            "$d=New-Object System.Windows.Forms.OpenFileDialog;" +
            "$d.Multiselect=$true;" +
            $"$d.Title='{title.Replace("'", "''")}';" +
            "$d.InitialDirectory=[Environment]::GetFolderPath('Desktop');" +
            "if($d.ShowDialog($owner) -eq [System.Windows.Forms.DialogResult]::OK){" +
            "[IO.File]::WriteAllLines('" + output.Replace("'", "''") + "',$d.FileNames,[Text.Encoding]::UTF8)}";
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        var powershell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        var arguments = $"-NoProfile -NonInteractive -STA -ExecutionPolicy Bypass -WindowStyle Hidden -EncodedCommand {encoded}";

        try
        {
            RunAsLoggedOnUser(powershell, arguments, TimeSpan.FromMinutes(10));
            if (!File.Exists(output))
            {
                return [];
            }
            return File.ReadAllLines(output)
                .Select(x => x.Trim().TrimStart('\uFEFF'))
                .Where(x => Path.IsPathFullyQualified(x) && File.Exists(x))
                .ToList();
        }
        catch
        {
            return [];
        }
        finally
        {
            try
            {
                File.Delete(output);
            }
            catch
            {
                // Arquivo temporário.
            }
        }
    }

    /// <summary>Abre um programa visível como o usuário logado, sem esperar (Explorador, arquivo recebido...).</summary>
    public static bool StartAsLoggedOnUser(string file, string arguments) =>
        RunAsLoggedOnUser(file, arguments, TimeSpan.Zero, visible: true);

    /// <summary>Roda um programa oculto como o usuário logado (ou direto, se já somos o usuário).</summary>
    private static bool RunAsLoggedOnUser(string file, string arguments, TimeSpan timeout, bool visible = false)
    {
        var token = GetUserToken();
        if (token == 0)
        {
            using var process = Process.Start(new ProcessStartInfo(file, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = !visible
            });
            if (timeout == TimeSpan.Zero)
            {
                return process is not null;
            }
            return process?.WaitForExit(timeout) == true;
        }

        nint environment = 0;
        try
        {
            CreateEnvironmentBlock(out environment, token, false);
            var startupInfo = new ADVAPI32.STARTUPINFO
            {
                cb = Marshal.SizeOf<ADVAPI32.STARTUPINFO>(),
                lpDesktop = @"winsta0\default"
            };
            var security = new ADVAPI32.SECURITY_ATTRIBUTES();
            security.Length = Marshal.SizeOf(security);
            var threadSecurity = new ADVAPI32.SECURITY_ATTRIBUTES();
            threadSecurity.Length = Marshal.SizeOf(threadSecurity);

            if (!ADVAPI32.CreateProcessAsUser(
                    token,
                    null,
                    $"\"{file}\" {arguments}",
                    ref security,
                    ref threadSecurity,
                    false,
                    (uint)(ADVAPI32.NORMAL_PRIORITY_CLASS | ADVAPI32.CREATE_UNICODE_ENVIRONMENT | (visible ? 0 : ADVAPI32.CREATE_NO_WINDOW)),
                    environment,
                    null,
                    ref startupInfo,
                    out var processInfo))
            {
                return false;
            }

            try
            {
                if (timeout == TimeSpan.Zero)
                {
                    return true;
                }
                return WaitForSingleObject(processInfo.hProcess, (uint)timeout.TotalMilliseconds) == 0;
            }
            finally
            {
                Kernel32.CloseHandle(processInfo.hThread);
                Kernel32.CloseHandle(processInfo.hProcess);
            }
        }
        finally
        {
            if (environment != 0)
            {
                DestroyEnvironmentBlock(environment);
            }
            Kernel32.CloseHandle(token);
        }
    }

    /// <summary>Token do usuário da sessão deste processo. 0 quando não roda como SYSTEM (modo assistido).</summary>
    private static nint GetUserToken()
    {
        try
        {
            var sessionId = (uint)Process.GetCurrentProcess().SessionId;
            return WTSQueryUserToken(sessionId, out var token) ? token : 0;
        }
        catch
        {
            return 0;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern nint WindowFromPoint(POINT point);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern nint GetAncestor(nint hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint hwnd, StringBuilder className, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint hwnd, StringBuilder text, int maxCount);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSQueryUserToken(uint sessionId, out nint token);

    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid folderId, uint flags, nint token, out nint path);

    [DllImport("userenv.dll", SetLastError = true)]
    private static extern bool CreateEnvironmentBlock(out nint environment, nint token, bool inherit);

    [DllImport("userenv.dll", SetLastError = true)]
    private static extern bool DestroyEnvironmentBlock(nint environment);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(nint handle, uint milliseconds);
}
