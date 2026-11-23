using System;
using System.Globalization;
using System.Text;

namespace Remotely.Agent.Services;

/// <summary>
/// Página de código das ferramentas de console do Windows (cmd.exe, powershell.exe, netsh...).
/// Elas escrevem na página OEM do sistema (850 em pt-BR); ler como UTF-8 ou Windows-1252
/// troca "versão" por "versÆo" e "Já" por "J ".
/// </summary>
internal static class ConsoleEncodings
{
    private static readonly Lazy<Encoding> _oem = new(() =>
    {
        if (!OperatingSystem.IsWindows())
        {
            return new UTF8Encoding(false);
        }

        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
        }
        catch
        {
            return new UTF8Encoding(false);
        }
    });

    public static Encoding Oem => _oem.Value;
}
