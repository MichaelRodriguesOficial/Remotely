using System.Runtime.Serialization;

namespace Remotely.Shared.Models.Dtos;

/// <summary>
/// Mensagem do chat entre o técnico e o usuário. Na sessão de acesso vai pelo canal da tela (anexos
/// com os bytes junto); no chat avulso vai pelo agente (anexos guardados no servidor, em FileId).
/// </summary>
[DataContract]
public class ChatMessageDto
{
    [DataMember(Name = "Id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [DataMember(Name = "SenderName")]
    public string SenderName { get; set; } = string.Empty;

    [DataMember(Name = "Text")]
    public string Text { get; set; } = string.Empty;

    /// <summary>ISO 8601.</summary>
    [DataMember(Name = "Timestamp")]
    public string Timestamp { get; set; } = DateTimeOffset.Now.ToString("O");

    [DataMember(Name = "Attachments")]
    public List<ChatAttachmentDto> Attachments { get; set; } = [];
}

[DataContract]
public class ChatAttachmentDto
{
    [DataMember(Name = "Id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [DataMember(Name = "FileName")]
    public string FileName { get; set; } = string.Empty;

    [DataMember(Name = "ContentType")]
    public string ContentType { get; set; } = "application/octet-stream";

    [DataMember(Name = "Size")]
    public long Size { get; set; }

    /// <summary>Conteúdo (chat na sessão). Vazio no chat avulso.</summary>
    [DataMember(Name = "Data")]
    public byte[]? Data { get; set; }

    /// <summary>Arquivo guardado no servidor (chat avulso).</summary>
    [DataMember(Name = "FileId")]
    public string? FileId { get; set; }

    public bool IsImage => ChatLimits.IsImage(ContentType, FileName);
}

/// <summary>"Digitando..." e "lida".</summary>
[DataContract]
public class ChatSignalDto
{
    [DataMember(Name = "SenderName")]
    public string SenderName { get; set; } = string.Empty;

    /// <summary>Na confirmação de leitura: a última mensagem lida.</summary>
    [DataMember(Name = "MessageId")]
    public string? MessageId { get; set; }
}

public static class ChatLimits
{
    /// <summary>Tamanho máximo de cada anexo.</summary>
    public const long MaxAttachmentBytes = 25 * 1024 * 1024;

    public const int MaxAttachmentsPerMessage = 10;

    public const int MaxTextLength = 4000;

    private static readonly Dictionary<string, string> _types = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".webp"] = "image/webp",
        [".bmp"] = "image/bmp",
        [".pdf"] = "application/pdf",
        [".txt"] = "text/plain",
        [".log"] = "text/plain",
        [".csv"] = "text/csv",
        [".zip"] = "application/zip",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        [".doc"] = "application/msword",
        [".xls"] = "application/vnd.ms-excel"
    };

    public static string ContentTypeFor(string fileName) =>
        _types.TryGetValue(Path.GetExtension(fileName ?? string.Empty), out var type) ? type : "application/octet-stream";

    public static bool IsImage(string? contentType, string? fileName) =>
        (contentType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true &&
         !contentType.Contains("svg", StringComparison.OrdinalIgnoreCase)) ||
        ContentTypeFor(fileName ?? string.Empty).StartsWith("image/", StringComparison.Ordinal);

    /// <summary>"1,2 MB".</summary>
    public static string FormatSize(long bytes) =>
        bytes >= 1024 * 1024 ? $"{bytes / 1024d / 1024d:0.#} MB" :
        bytes >= 1024 ? $"{bytes / 1024d:0} KB" :
        $"{bytes} B";
}
