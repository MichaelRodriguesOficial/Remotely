using System.Text;

namespace Remotely.Shared.Utilities;

/// <summary>
/// Monta argumentos de linha de comando do Windows com as regras do CommandLineToArgvW.
/// Sem isso, um nome com aspas (ex.: o nome do técnico) podia acrescentar argumentos ao processo.
/// </summary>
public static class CommandLineQuoting
{
    public static string Quote(string? value)
    {
        value ??= string.Empty;
        if (value.Length > 0 && value.All(c => !char.IsWhiteSpace(c) && c != '"'))
        {
            return value;
        }

        var builder = new StringBuilder(value.Length + 2);
        builder.Append('"');
        var backslashes = 0;
        foreach (var c in value)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }

            if (c == '"')
            {
                builder.Append('\\', backslashes * 2 + 1);
                builder.Append('"');
            }
            else
            {
                builder.Append('\\', backslashes);
                builder.Append(c);
            }
            backslashes = 0;
        }

        // Barras antes da aspa de fechamento precisam ser dobradas.
        builder.Append('\\', backslashes * 2);
        builder.Append('"');
        return builder.ToString();
    }

    public static string Join(IEnumerable<string?> arguments) => string.Join(' ', arguments.Select(Quote));
}
