using System.Security.Cryptography;
using Microsoft.Extensions.FileProviders;

namespace Remotely.Server.Services;

/// <summary>
/// Os scripts do visualizador (/src) são módulos ES: o App.js importa os outros por caminho relativo.
/// Um "?v=" só no App.js não basta, e proxies como o do aaPanel guardam .js por horas. Por isso o
/// visualizador carrega tudo de /src/v/{versão}/..., que o servidor reescreve para /src/.... Cada
/// versão tem um caminho próprio e o navegador nunca mistura JavaScript antigo com HTML novo.
/// </summary>
public static class ViewerAssets
{
    public const string VersionedPrefix = "/src/v/";

    private static string? _version;
    private static readonly object _lock = new();

    /// <summary>Hash curto do conteúdo de wwwroot/src/*.js (muda a cada build que altera o visualizador).</summary>
    public static string GetVersion(IWebHostEnvironment env)
    {
        if (_version is not null)
        {
            return _version;
        }

        lock (_lock)
        {
            if (_version is not null)
            {
                return _version;
            }

            try
            {
                using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                AddDirectory(env.WebRootFileProvider, "src", sha);
                _version = Convert.ToHexString(sha.GetHashAndReset())[..12].ToLowerInvariant();
            }
            catch
            {
                _version = typeof(ViewerAssets).Assembly.GetName().Version?.ToString() ?? "1";
            }

            return _version;
        }
    }

    private static void AddDirectory(IFileProvider provider, string path, IncrementalHash sha)
    {
        foreach (var entry in provider.GetDirectoryContents(path).OrderBy(x => x.Name, StringComparer.Ordinal))
        {
            var entryPath = $"{path}/{entry.Name}";
            if (entry.IsDirectory)
            {
                AddDirectory(provider, entryPath, sha);
                continue;
            }

            if (!entry.Name.EndsWith(".js", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            sha.AppendData(System.Text.Encoding.UTF8.GetBytes(entryPath));
            using var stream = entry.CreateReadStream();
            sha.AppendData(SHA256.HashData(stream));
        }
    }

    /// <summary>
    /// /src/v/{versão}/UI.js → /src/UI.js, com cache longo (o caminho muda a cada versão).
    /// /src/*.js sem versão: o navegador confere com o servidor antes de usar a cópia guardada.
    /// </summary>
    public static void UseViewerAssetVersioning(this IApplicationBuilder app)
    {
        app.Use(async (context, next) =>
        {
            var path = context.Request.Path.Value;
            if (path is not null && path.StartsWith("/src/", StringComparison.OrdinalIgnoreCase))
            {
                var versioned = false;
                if (path.StartsWith(VersionedPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    var rest = path[VersionedPrefix.Length..];
                    var slash = rest.IndexOf('/');
                    if (slash > 0)
                    {
                        context.Request.Path = "/src/" + rest[(slash + 1)..];
                        versioned = true;
                    }
                }

                context.Response.OnStarting(() =>
                {
                    if (context.Response.StatusCode == StatusCodes.Status200OK ||
                        context.Response.StatusCode == StatusCodes.Status304NotModified)
                    {
                        context.Response.Headers.CacheControl = versioned
                            ? "public, max-age=31536000, immutable"
                            : "no-cache";
                        context.Response.Headers.Remove("Expires");
                    }
                    return Task.CompletedTask;
                });
            }

            await next();
        });
    }
}
