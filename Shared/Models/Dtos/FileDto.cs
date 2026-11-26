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
}
