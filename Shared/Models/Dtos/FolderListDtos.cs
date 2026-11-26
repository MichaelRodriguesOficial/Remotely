using System.Runtime.Serialization;

namespace Remotely.Shared.Models.Dtos;

[DataContract]
public class FolderListRequestDto
{
    /// <summary>Caminho a listar. Vazio = só os atalhos (Área de Trabalho, Downloads, unidades).</summary>
    [DataMember(Name = "Path")]
    public string Path { get; set; } = string.Empty;
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
}
