namespace Remotely.Server.Utilities;

/// <summary>
/// Lê um arquivo seguido de alguns bytes extras, sem copiar o arquivo para a memória.
/// Usado para anexar os dados do servidor ao final do cliente de suporte no download.
/// </summary>
public sealed class AppendedDataStream : Stream
{
    private readonly FileStream _file;
    private readonly byte[] _suffix;
    private readonly long _fileLength;
    private long _position;

    public AppendedDataStream(string filePath, byte[] suffix)
    {
        _file = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        _fileLength = _file.Length;
        _suffix = suffix;
    }

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => _fileLength + _suffix.Length;

    public override long Position
    {
        get => _position;
        set => Seek(value, SeekOrigin.Begin);
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        if (buffer.Length == 0 || _position >= Length)
        {
            return 0;
        }

        if (_position < _fileLength)
        {
            _file.Position = _position;
            var toRead = (int)Math.Min(buffer.Length, _fileLength - _position);
            var read = _file.Read(buffer[..toRead]);
            _position += read;
            return read;
        }

        return ReadSuffix(buffer);
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (buffer.Length == 0 || _position >= Length)
        {
            return 0;
        }

        if (_position < _fileLength)
        {
            _file.Position = _position;
            var toRead = (int)Math.Min(buffer.Length, _fileLength - _position);
            var read = await _file.ReadAsync(buffer[..toRead], cancellationToken);
            _position += read;
            return read;
        }

        return ReadSuffix(buffer.Span);
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    private int ReadSuffix(Span<byte> buffer)
    {
        var suffixOffset = (int)(_position - _fileLength);
        var toCopy = Math.Min(buffer.Length, _suffix.Length - suffixOffset);
        _suffix.AsSpan(suffixOffset, toCopy).CopyTo(buffer);
        _position += toCopy;
        return toCopy;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        var target = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => Length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin))
        };

        if (target < 0)
        {
            throw new IOException("Posição antes do início do arquivo.");
        }

        _position = target;
        return _position;
    }

    public override void Flush()
    {
    }

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _file.Dispose();
        }
        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        await _file.DisposeAsync();
        await base.DisposeAsync();
    }
}
