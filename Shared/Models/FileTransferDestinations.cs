namespace Remotely.Shared.Models;

/// <summary>
/// Destinos aceitos ao enviar arquivos do painel para um computador.
/// O valor trafega como texto: um dos códigos abaixo ou "path:" seguido de um caminho absoluto local.
/// Servidor e agente validam com <see cref="TryParse"/>.
/// </summary>
public static class FileTransferDestinations
{
    /// <summary>Pasta Downloads do usuário logado (ou a pública, se ninguém estiver logado).</summary>
    public const string Downloads = "downloads";

    /// <summary>Área de Trabalho do usuário logado (respeita o redirecionamento do OneDrive).</summary>
    public const string Desktop = "desktop";

    /// <summary>Área de Trabalho pública (C:\Users\Public\Desktop): aparece para todos os usuários. Só Windows.</summary>
    public const string PublicDesktop = "public-desktop";

    /// <summary>Pasta temporária do agente (comportamento antigo do Remotely).</summary>
    public const string Temp = "temp";

    /// <summary>Prefixo de uma pasta escolhida pelo técnico.</summary>
    public const string PathPrefix = "path:";

    public const int MaxPathLength = 240;

    public static string ForPath(string path) => PathPrefix + path.Trim();

    /// <summary>
    /// Confere o destino. Em caso de sucesso, <paramref name="kind"/> é um dos códigos e
    /// <paramref name="customPath"/> traz a pasta quando o destino é "path:".
    /// </summary>
    public static bool TryParse(string? destination, bool isWindows, out string kind, out string? customPath, out string error)
    {
        kind = string.Empty;
        customPath = null;
        error = string.Empty;

        var value = destination?.Trim() ?? string.Empty;
        if (value.Length == 0)
        {
            kind = Downloads;
            return true;
        }

        switch (value)
        {
            case Downloads:
            case Desktop:
            case Temp:
                kind = value;
                return true;
            case PublicDesktop:
                if (!isWindows)
                {
                    error = "A Área de Trabalho pública só existe no Windows.";
                    return false;
                }
                kind = value;
                return true;
        }

        if (!value.StartsWith(PathPrefix, StringComparison.Ordinal))
        {
            error = "Destino desconhecido.";
            return false;
        }

        var path = value[PathPrefix.Length..].Trim();
        if (!IsValidCustomPath(path, isWindows, out error))
        {
            return false;
        }

        kind = PathPrefix;
        customPath = path;
        return true;
    }

    /// <summary>Valida uma pasta digitada pelo técnico: absoluta, local, sem "..", sem caracteres inválidos.</summary>
    public static bool IsValidCustomPath(string? path, bool isWindows, out string error)
    {
        error = string.Empty;
        path = path?.Trim() ?? string.Empty;

        if (path.Length == 0)
        {
            error = "Informe a pasta de destino.";
            return false;
        }

        if (path.Length > MaxPathLength)
        {
            error = $"O caminho passa de {MaxPathLength} caracteres.";
            return false;
        }

        if (path.Any(c => char.IsControl(c) || c is '"' or '<' or '>' or '|' or '?' or '*'))
        {
            error = "O caminho tem caracteres inválidos.";
            return false;
        }

        if (isWindows)
        {
            if (path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal))
            {
                error = "Use uma pasta local (ex.: C:\\Suporte). Caminhos de rede não são aceitos.";
                return false;
            }

            var hasDrive = path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] is '\\' or '/';
            if (!hasDrive || path.IndexOf(':', 2) >= 0)
            {
                error = "Use um caminho completo com a letra da unidade, como C:\\Suporte.";
                return false;
            }
        }
        else if (!path.StartsWith('/'))
        {
            error = "Use um caminho completo, começando com /.";
            return false;
        }

        var segments = path.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(s => s == ".." || s == "."))
        {
            error = "O caminho não pode ter \"..\" nem \".\".";
            return false;
        }

        return true;
    }

    /// <summary>Texto curto para mostrar o destino ao técnico.</summary>
    public static string Describe(string? destination)
    {
        var value = destination?.Trim() ?? string.Empty;
        return value switch
        {
            "" or Downloads => "Downloads do usuário",
            Desktop => "Área de Trabalho do usuário",
            PublicDesktop => "Área de Trabalho pública",
            Temp => "Pasta temporária do agente",
            _ when value.StartsWith(PathPrefix, StringComparison.Ordinal) => value[PathPrefix.Length..],
            _ => value
        };
    }
}
