namespace Remotely.Server.Extensions;

/// <summary>
/// Descobre o esquema (http/https) que o navegador realmente usou para chegar
/// ao servidor. Atrás de proxies que não enviam X-Forwarded-Proto (por exemplo,
/// o proxy reverso padrão do aaPanel) o ASP.NET enxerga apenas "http", o que
/// faria os clientes e instaladores apontarem para http://.
/// </summary>
public static class HttpRequestSchemeExtensions
{
    public static string GetEffectiveScheme(this HttpRequest request, bool forceHttps)
    {
        if (forceHttps || request.IsHttps)
        {
            return "https";
        }

        var headers = request.Headers;

        if (IsHttps(headers["X-Forwarded-Proto"].ToString()) ||
            IsHttps(headers["X-Forwarded-Scheme"].ToString()) ||
            IsHttps(headers["X-Scheme"].ToString()) ||
            string.Equals(headers["X-Forwarded-Ssl"].ToString(), "on", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(headers["Front-End-Https"].ToString(), "on", StringComparison.OrdinalIgnoreCase))
        {
            return "https";
        }

        // Dica explícita enviada pela página (?scheme=https). Só aceita subir
        // para https, nunca descer para http.
        if (IsHttps(request.Query["scheme"].ToString()))
        {
            return "https";
        }

        // O navegador informa a página de origem do clique. Se ela é https e
        // está no mesmo host, o servidor está publicado em https.
        var referer = headers.Referer.ToString();
        if (Uri.TryCreate(referer, UriKind.Absolute, out var refUri) &&
            refUri.Scheme == Uri.UriSchemeHttps &&
            string.Equals(refUri.Host, request.Host.Host, StringComparison.OrdinalIgnoreCase))
        {
            return "https";
        }

        return request.Scheme;
    }

    private static bool IsHttps(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }
        // X-Forwarded-Proto pode trazer uma lista ("https, http").
        var first = value.Split(',')[0].Trim();
        return string.Equals(first, "https", StringComparison.OrdinalIgnoreCase);
    }
}
