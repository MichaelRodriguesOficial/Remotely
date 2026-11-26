using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using Remotely.Desktop.Shared.Reactive;

namespace Remotely.Desktop.UI.ViewModels;

/// <summary>Um arquivo na janela de progresso.</summary>
public class FileTransferProgressItem : ObservableObject
{
    public string Name { get; init; } = string.Empty;
    public string SizeText { get; init; } = string.Empty;

    public string StatusText
    {
        get => Get<string>() ?? "Aguardando";
        set => Set(value);
    }

    public bool IsDone
    {
        get => Get<bool>();
        set => Set(value);
    }
}

/// <summary>Janela na tela do usuário com os arquivos sendo transferidos, progresso, tempo e Cancelar.</summary>
public class FileTransferProgressViewModel : ObservableObject
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
    private readonly DateTimeOffset _startedAt = DateTimeOffset.Now;
    private readonly Action _cancel;
    private long _bytes;
    private long _totalBytes;

    public FileTransferProgressViewModel(bool incoming, string technicianName, long totalBytes, int fileCount, Action cancel)
    {
        _cancel = cancel;
        _totalBytes = Math.Max(0, totalBytes);
        Incoming = incoming;
        Title = incoming ? "Recebendo arquivos" : "Copiando arquivos para o técnico";
        Subtitle = string.IsNullOrWhiteSpace(technicianName)
            ? (incoming ? "O suporte de TI está enviando arquivos para este computador." : "O suporte de TI está copiando arquivos deste computador.")
            : (incoming ? $"{technicianName} está enviando arquivos para este computador." : $"{technicianName} está copiando arquivos deste computador.");
        FileCount = fileCount;
        IsRunning = true;
        CancelCommand = new RelayCommand(Cancel);
        CloseCommand = new RelayCommand(() => CloseRequested?.Invoke(this, EventArgs.Empty));
        UpdateDetail();
    }

    public event EventHandler? CloseRequested;

    public bool Incoming { get; }
    public int FileCount { get; }
    public string Title { get => Get<string>() ?? string.Empty; private set => Set(value); }
    public string Subtitle { get; }
    public ObservableCollection<FileTransferProgressItem> Files { get; } = [];
    public double Percent { get => Get<double>(); private set => Set(value); }
    public string DetailText { get => Get<string>() ?? string.Empty; private set => Set(value); }
    public bool IsRunning { get => Get<bool>(); private set => Set(value); }
    public bool IsFinished => !IsRunning;
    public bool IsCanceling { get => Get<bool>(); private set => Set(value); }
    public ICommand CancelCommand { get; }
    public ICommand CloseCommand { get; }

    public void FileStarted(string name, long size)
    {
        var existing = Files.FirstOrDefault(x => x.Name == name && !x.IsDone);
        var item = existing ?? new FileTransferProgressItem { Name = name, SizeText = FormatSize(size) };
        item.StatusText = Incoming ? "Recebendo..." : "Enviando...";
        if (existing is null)
        {
            Files.Add(item);
        }
        if (_totalBytes <= 0)
        {
            _totalBytes = size;
        }
    }

    public void AddBytes(long bytes)
    {
        _bytes += bytes;
        UpdateDetail();
    }

    public void FileFinished(string name, bool success)
    {
        var item = Files.LastOrDefault(x => x.Name == name && !x.IsDone);
        if (item is not null)
        {
            item.IsDone = true;
            item.StatusText = success ? "Concluído" : "Cancelado";
        }
    }

    public void Complete(string? message)
    {
        IsRunning = false;
        IsCanceling = false;
        NotifyPropertyChanged(nameof(IsFinished));
        if (message is null)
        {
            Percent = 100;
            Title = Incoming ? "Arquivos recebidos" : "Arquivos copiados";
            DetailText = $"{FormatSize(_bytes)} em {FormatDuration(DateTimeOffset.Now - _startedAt)}.";
        }
        else
        {
            Title = message;
            DetailText = "Os arquivos incompletos foram descartados.";
            foreach (var item in Files.Where(x => !x.IsDone))
            {
                item.IsDone = true;
                item.StatusText = "Cancelado";
            }
        }
    }

    private void Cancel()
    {
        if (!IsRunning || IsCanceling)
        {
            return;
        }
        IsCanceling = true;
        DetailText = "Cancelando...";
        _cancel();
    }

    private void UpdateDetail()
    {
        if (IsCanceling)
        {
            return;
        }

        var total = Math.Max(_totalBytes, _bytes);
        Percent = total > 0 ? Math.Min(100, 100.0 * _bytes / total) : 0;
        var elapsed = DateTimeOffset.Now - _startedAt;
        var text = total > 0 ? $"{FormatSize(_bytes)} de {FormatSize(total)}" : FormatSize(_bytes);
        if (_bytes > 0 && total > _bytes && elapsed.TotalSeconds >= 1)
        {
            var speed = _bytes / elapsed.TotalSeconds;
            var remaining = TimeSpan.FromSeconds((total - _bytes) / Math.Max(speed, 1));
            text += $" · cerca de {FormatDuration(remaining)} restantes";
        }
        DetailText = text;
    }

    internal static string FormatSize(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }
        if (bytes < 1024 * 1024)
        {
            return (bytes / 1024.0).ToString("0.#", PtBr) + " KB";
        }
        if (bytes < 1024L * 1024 * 1024)
        {
            return (bytes / 1024.0 / 1024).ToString("0.#", PtBr) + " MB";
        }
        return (bytes / 1024.0 / 1024 / 1024).ToString("0.##", PtBr) + " GB";
    }

    internal static string FormatDuration(TimeSpan time)
    {
        if (time.TotalSeconds < 60)
        {
            return $"{Math.Max(1, (int)Math.Ceiling(time.TotalSeconds))} s";
        }
        if (time.TotalMinutes < 60)
        {
            return $"{(int)time.TotalMinutes} min {time.Seconds:00} s";
        }
        return $"{(int)time.TotalHours} h {time.Minutes:00} min";
    }
}
