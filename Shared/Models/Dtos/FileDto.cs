using System.Runtime.Serialization;

namespace Remotely.Shared.Models.Dtos;


[DataContract]
public class FileDto
{
    [DataMember(Name = "Buffer")]
    public byte[] Buffer { get; set; } = Array.Empty<byte>();

    [DataMember(Name = "FileName")]
    public string FileName { get; set; } = string.Empty;

    [DataMember(Name = "MessageId")]
    public string MessageId { get; set; } = string.Empty;

    [DataMember(Name = "EndOfFile")]
    public bool EndOfFile { get; set; }

    [DataMember(Name = "StartOfFile")]
    public bool StartOfFile { get; set; }

    /// <summary>
    /// Onde salvar (só no primeiro pedaço): vazio = padrão, "drop:x;y" = pasta sob o ponto onde o arquivo
    /// foi solto na tela (0 a 1), "path:C:\pasta" = pasta escolhida no visualizador.
    /// </summary>
    [DataMember(Name = "Destination")]
    public string? Destination { get; set; }

    /// <summary>Tamanho do arquivo (no primeiro pedaço), para a barra de progresso.</summary>
    [DataMember(Name = "FileSize")]
    public long FileSize { get; set; }

    /// <summary>Envio de vários arquivos de uma vez: mesmo ID, total de bytes e de arquivos.</summary>
    [DataMember(Name = "BatchId")]
    public string? BatchId { get; set; }

    [DataMember(Name = "BatchTotalBytes")]
    public long BatchTotalBytes { get; set; }

    [DataMember(Name = "BatchFileCount")]
    public int BatchFileCount { get; set; }

    /// <summary>A transferência foi cancelada: descartar o que chegou deste arquivo.</summary>
    [DataMember(Name = "Canceled")]
    public bool Canceled { get; set; }
}
