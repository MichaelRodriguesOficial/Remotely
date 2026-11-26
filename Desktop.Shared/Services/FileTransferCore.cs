using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Remotely.Desktop.Shared.Abstractions;
using Remotely.Desktop.Shared.ViewModels;
using Remotely.Shared.Models.Dtos;

namespace Remotely.Desktop.Shared.Services;

/// <summary>
/// Recebimento e envio de arquivos entre o visualizador e este computador (Windows e Linux),
/// com a janela de progresso na tela do usuário e o botão Cancelar.
/// </summary>
public static class FileTransferCore
{
    private sealed class PartialFile
    {
        public required FileStream Stream { get; init; }
        public required string Path { get; init; }
        public required string FileName { get; init; }
        public required string BatchId { get; init; }
    }

    private sealed class Batch
    {
        public required IFileTransferProgress Progress { get; init; }
        public int FileCount { get; init; }
        public int Finished { get; set; }
        public bool Canceled { get; set; }
    }

    private static readonly ConcurrentDictionary<string, PartialFile> _partials = new();
    private static readonly ConcurrentDictionary<string, Batch> _batches = new();
    private static readonly SemaphoreSlim _writeLock = new(1, 1);

    /// <summary>Recebe um pedaço de arquivo. <paramref name="resolveDirectory"/> escolhe a pasta no primeiro pedaço.</summary>
    public static async Task ReceiveFile(
        FileDto dto,
        IViewer viewer,
        Func<string?, IViewer, string> resolveDirectory,
        Func<string> fallbackDirectory,
        IFileTransferProgressService progressService,
        ILogger logger)
    {
        var batchId = string.IsNullOrWhiteSpace(dto.BatchId) ? dto.MessageId : dto.BatchId!;
        FileSavedDto? notice = null;

        await _writeLock.WaitAsync();
        try
        {
            if (_batches.TryGetValue(batchId, out var canceledBatch) && canceledBatch.Canceled)
            {
                // O usuário já cancelou este lote: ignora o resto que ainda estava a caminho.
                return;
            }

            if (dto.StartOfFile)
            {
                var batch = _batches.GetOrAdd(batchId, _ => new Batch
                {
                    Progress = progressService.Begin(
                        incoming: true,
                        viewer.Name,
                        dto.BatchTotalBytes > 0 ? dto.BatchTotalBytes : dto.FileSize,
                        Math.Max(1, dto.BatchFileCount)),
                    FileCount = Math.Max(1, dto.BatchFileCount)
                });

                var safeName = ReceivedFiles.SafeFileName(dto.FileName);
                var directory = resolveDirectory(dto.Destination, viewer);
                FileStream stream;
                string filePath;
                try
                {
                    filePath = ReceivedFiles.GetAvailablePath(directory, safeName);
                    stream = new FileStream(filePath, FileMode.CreateNew, FileAccess.Write);
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
                {
                    logger.LogWarning(ex, "Could not write to {directory}. Using the fallback folder.", directory);
                    filePath = ReceivedFiles.GetAvailablePath(fallbackDirectory(), safeName);
                    stream = new FileStream(filePath, FileMode.CreateNew, FileAccess.Write);
                }

                _partials[dto.MessageId] = new PartialFile { Stream = stream, Path = filePath, FileName = safeName, BatchId = batchId };
                batch.Progress.FileStarted(safeName, dto.FileSize);
            }

            if (!_partials.TryGetValue(dto.MessageId, out var partial) ||
                !_batches.TryGetValue(partial.BatchId, out var current))
            {
                return;
            }

            if (current.Progress.CancellationToken.IsCancellationRequested || dto.Canceled)
            {
                current.Canceled = true;
                await DiscardAll(partial.BatchId);
                current.Progress.Complete(dto.Canceled ? "O técnico cancelou o envio." : "Envio cancelado.");
                notice = new FileSavedDto
                {
                    FileName = partial.FileName,
                    Success = false,
                    Canceled = true,
                    BatchId = partial.BatchId,
                    Message = dto.Canceled ? "Envio cancelado." : "O usuário cancelou o envio dos arquivos."
                };
                return;
            }

            if (dto.Buffer?.Length > 0)
            {
                await partial.Stream.WriteAsync(dto.Buffer);
                current.Progress.AddBytes(dto.Buffer.Length);
            }

            if (dto.EndOfFile)
            {
                await partial.Stream.DisposeAsync();
                _partials.TryRemove(dto.MessageId, out _);
                current.Progress.FileFinished(partial.FileName, true);
                current.Finished++;
                if (current.Finished >= current.FileCount)
                {
                    current.Progress.Complete();
                    _batches.TryRemove(partial.BatchId, out _);
                }

                notice = new FileSavedDto { FileName = partial.FileName, FullPath = partial.Path, Success = true, BatchId = partial.BatchId };
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error while receiving file.");
            if (_partials.TryRemove(dto.MessageId, out var broken))
            {
                await broken.Stream.DisposeAsync();
            }
            notice = new FileSavedDto
            {
                FileName = ReceivedFiles.SafeFileName(dto.FileName),
                Success = false,
                BatchId = batchId,
                Message = "Não foi possível salvar o arquivo no computador."
            };
        }
        finally
        {
            _writeLock.Release();
        }

        if (notice is not null)
        {
            await viewer.SendDto(notice, DtoType.FileSaved);
        }
    }

    /// <summary>Apaga os arquivos incompletos do lote cancelado.</summary>
    private static async Task DiscardAll(string batchId)
    {
        foreach (var (messageId, partial) in _partials.Where(x => x.Value.BatchId == batchId).ToList())
        {
            _partials.TryRemove(messageId, out _);
            try
            {
                await partial.Stream.DisposeAsync();
                File.Delete(partial.Path);
            }
            catch
            {
                // Arquivo em uso: fica para o usuário apagar.
            }
        }
    }

    /// <summary>"Baixar arquivos" e arquivos copiados: envia ao técnico com a janela de progresso.</summary>
    public static async Task SendFilesToViewer(
        IReadOnlyList<string> paths,
        IViewer viewer,
        IFileTransferProgressService progressService,
        ILogger logger)
    {
        var files = ReceivedFiles.DescribeFiles(paths);
        if (files.Count == 0)
        {
            await viewer.SendDto(new FileSavedDto { Success = false, Message = "Arquivo não encontrado no computador." }, DtoType.FileSaved);
            return;
        }

        using var progress = progressService.Begin(incoming: false, viewer.Name, files.Sum(x => x.Size), files.Count);
        var canceled = false;
        try
        {
            foreach (var file in files)
            {
                if (progress.CancellationToken.IsCancellationRequested)
                {
                    canceled = true;
                    break;
                }

                progress.FileStarted(file.Name, file.Size);
                long sent = 0;
                var upload = new FileUpload { FilePath = file.Path };
                await viewer.SendFile(upload, fraction =>
                {
                    var bytes = (long)(fraction * file.Size);
                    progress.AddBytes(Math.Max(0, bytes - sent));
                    sent = Math.Max(sent, bytes);
                }, progress.CancellationToken);

                canceled = progress.CancellationToken.IsCancellationRequested;
                progress.FileFinished(file.Name, !canceled);
                if (canceled)
                {
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error while sending files to the viewer.");
            progress.Complete("Não foi possível enviar os arquivos.");
            return;
        }

        if (canceled)
        {
            progress.Complete("Envio cancelado.");
            await viewer.SendDto(new FileSavedDto { Success = false, Canceled = true, Message = "O usuário cancelou a cópia dos arquivos." }, DtoType.FileSaved);
        }
        else
        {
            progress.Complete();
        }
    }
}
