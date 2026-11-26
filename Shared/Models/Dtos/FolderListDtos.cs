using System.Runtime.Serialization;

namespace Remotely.Shared.Models.Dtos;

[DataContract]
public class FolderListRequestDto
{
    /// <summary>Caminho a listar. Vazio = só os atalhos (Área de Trabalho, Downloads, unidades).</summary>
    [DataMember(Name = "Path")]
    public string Path { get; set; } = string.Empty;

    /// <summary>Também os arquivos (para "Baixar arquivos").</summary>
    [DataMember(Name = "IncludeFiles")]
    public bool IncludeFiles { get; set; }
}

[DataContract]
public class FileItemDto
{
    [DataMember(Name = "Name")]
    public string Name { get; set; } = string.Empty;

    [DataMember(Name = "Path")]
    public string Path { get; set; } = string.Empty;

    [DataMember(Name = "Size")]
    public long Size { get; set; }

    [DataMember(Name = "Modified")]
    public string? Modified { get; set; }
}

/// <summary>Arquivos copiados no computador remoto, ou pedidos pelo visualizador.</summary>
[DataContract]
public class FileListDto
{
    [DataMember(Name = "Files")]
    public List<FileItemDto> Files { get; set; } = [];
}

[DataContract]
public class FolderItemDto
{
    [DataMember(Name = "Name")]
    public string Name { get; set; } = string.Empty;

    [DataMember(Name = "Path")]
    public string Path { get; set; } = string.Empty;

    /// <summary>desktop, downloads, documents, public, drive, folder.</summary>
    [DataMember(Name = "Kind")]
    public string Kind { get; set; } = "folder";
}

[DataContract]
public class FolderListDto
{
    [DataMember(Name = "Path")]
    public string Path { get; set; } = string.Empty;

    [DataMember(Name = "ParentPath")]
    public string? ParentPath { get; set; }

    [DataMember(Name = "Folders")]
    public List<FolderItemDto> Folders { get; set; } = [];

    [DataMember(Name = "Places")]
    public List<FolderItemDto> Places { get; set; } = [];

    [DataMember(Name = "Files")]
    public List<FileItemDto> Files { get; set; } = [];

    [DataMember(Name = "Error")]
    public string? Error { get; set; }
}

[DataContract]
public class FileSavedDto
{
    [DataMember(Name = "FileName")]
    public string FileName { get; set; } = string.Empty;

    [DataMember(Name = "FullPath")]
    public string FullPath { get; set; } = string.Empty;

    [DataMember(Name = "Success")]
    public bool Success { get; set; }

    [DataMember(Name = "Message")]
    public string? Message { get; set; }

    /// <summary>O usuário cancelou: o visualizador para de enviar o resto deste lote.</summary>
    [DataMember(Name = "Canceled")]
    public bool Canceled { get; set; }

    [DataMember(Name = "BatchId")]
    public string? BatchId { get; set; }
}
