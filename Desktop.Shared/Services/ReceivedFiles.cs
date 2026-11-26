using System.Globalization;
using Remotely.Shared.Models.Dtos;

namespace Remotely.Desktop.Shared.Services;

/// <summary>Regras comuns para salvar arquivos vindos do visualizador e listar pastas (Windows e Linux).</summary>
public static class ReceivedFiles
{
    public const string DropPrefix = "drop:";
    public const string PathPrefix = "path:";
    /// <summary>Colado com Ctrl+V no visualizador: a pasta da janela em primeiro plano.</summary>
    public const string PasteDestination = "paste";

    /// <summary>Só o nome do arquivo: um nome com "..\" ou caminho completo não escapa da pasta escolhida.</summary>
    public static string SafeFileName(string? fileName)
    {
        var name = Path.GetFileName((fileName ?? string.Empty).Replace('\\', '/').Split('/').LastOrDefault() ?? string.Empty);
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '_');
        }
        name = name.Trim().TrimEnd('.');
        return string.IsNullOrWhiteSpace(name) ? "arquivo" : name;
    }

    /// <summary>"relatorio.pdf" já existe → "relatorio (2).pdf". Nada é sobrescrito.</summary>
    public static string GetAvailablePath(string directory, string fileName)
    {
        var path = Path.Combine(directory, fileName);
        var name = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);
        for (var i = 2; File.Exists(path) || Directory.Exists(path); i++)
        {
            path = Path.Combine(directory, $"{name} ({i}){ext}");
        }
        return path;
    }

    /// <summary>"drop:0.42;0.77" → (0.42, 0.77).</summary>
    public static bool TryParseDrop(string? destination, out double percentX, out double percentY)
    {
        percentX = percentY = 0;
        if (destination?.StartsWith(DropPrefix, StringComparison.OrdinalIgnoreCase) != true)
        {
            return false;
        }

        var parts = destination[DropPrefix.Length..].Split(';');
        return parts.Length == 2 &&
            double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out percentX) &&
            double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out percentY) &&
            percentX is >= 0 and <= 1 && percentY is >= 0 and <= 1;
    }

    /// <summary>"path:C:\pasta" → pasta existente, ou null.</summary>
    public static string? TryParsePath(string? destination)
    {
        if (destination?.StartsWith(PathPrefix, StringComparison.OrdinalIgnoreCase) != true)
        {
            return null;
        }

        var path = destination[PathPrefix.Length..].Trim();
        return Path.IsPathFullyQualified(path) && Directory.Exists(path) ? path : null;
    }

    /// <summary>Subpastas de <paramref name="path"/> (sem as ocultas/de sistema) e os atalhos.</summary>
    public static FolderListDto BuildListing(string? path, List<FolderItemDto> places, bool includeFiles = false)
    {
        var listing = new FolderListDto { Places = places };
        if (string.IsNullOrWhiteSpace(path))
        {
            listing.Path = string.Empty;
            return listing;
        }

        if (!Path.IsPathFullyQualified(path) || !Directory.Exists(path))
        {
            listing.Path = path;
            listing.Error = "Pasta não encontrada.";
            return listing;
        }

        var directory = new DirectoryInfo(path);
        listing.Path = directory.FullName;
        listing.ParentPath = directory.Parent?.FullName;

        try
        {
            listing.Folders = directory
                .EnumerateDirectories()
                .Where(x =>
                {
                    try
                    {
                        return (x.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0;
                    }
                    catch
                    {
                        return false;
                    }
                })
                .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
                .Take(500)
                .Select(x => new FolderItemDto { Name = x.Name, Path = x.FullName })
                .ToList();

            if (includeFiles)
            {
                listing.Files = directory
                    .EnumerateFiles()
                    .Where(x =>
                    {
                        try
                        {
                            return (x.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0;
                        }
                        catch
                        {
                            return false;
                        }
                    })
                    .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
                    .Take(1000)
                    .Select(x => new FileItemDto
                    {
                        Name = x.Name,
                        Path = x.FullName,
                        Size = x.Length,
                        Modified = x.LastWriteTime.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture)
                    })
                    .ToList();
            }
        }
        catch (UnauthorizedAccessException)
        {
            listing.Error = "Sem permissão para ver esta pasta.";
        }
        catch (Exception)
        {
            listing.Error = "Não foi possível abrir esta pasta.";
        }

        return listing;
    }

    /// <summary>Arquivos existentes (sem pastas) com tamanho, para enviar ao técnico.</summary>
    public static List<FileItemDto> DescribeFiles(IEnumerable<string> paths)
    {
        var files = new List<FileItemDto>();
        foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase).Take(100))
        {
            try
            {
                if (!Path.IsPathFullyQualified(path) || !File.Exists(path))
                {
                    continue;
                }
                var info = new FileInfo(path);
                files.Add(new FileItemDto { Name = info.Name, Path = info.FullName, Size = info.Length });
            }
            catch
            {
                // Sem acesso ao arquivo.
            }
        }
        return files;
    }

    public static void AddDrives(List<FolderItemDto> places)
    {
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady || drive.DriveType is DriveType.CDRom or DriveType.Unknown or DriveType.NoRootDirectory)
                {
                    continue;
                }

                var label = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? "Disco" : drive.VolumeLabel;
                var name = OperatingSystem.IsWindows() ? $"{label} ({drive.Name.TrimEnd('\\')})" : drive.Name;
                if (!OperatingSystem.IsWindows() && drive.Name != "/")
                {
                    continue;
                }
                places.Add(new FolderItemDto { Name = name, Path = drive.RootDirectory.FullName, Kind = "drive" });
            }
            catch
            {
                // Unidade sumiu no meio da listagem.
            }
        }
    }
}
