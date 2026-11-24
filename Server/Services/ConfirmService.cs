namespace Remotely.Server.Services;

/// <summary>Janela de confirmação no padrão visual do painel (substitui o confirm/prompt do navegador).</summary>
public sealed record ConfirmOptions(
    string Title,
    string Message,
    string ConfirmText = "Confirmar",
    string CancelText = "Cancelar",
    bool Danger = false,
    string Icon = "fa-solid fa-circle-question")
{
    /// <summary>Quando preenchido, a janela pede um texto (ex.: novo nome) e devolve em <see cref="ConfirmResult.Text"/>.</summary>
    public string? InputLabel { get; init; }

    public string? InputValue { get; init; }
}

public sealed record ConfirmResult(bool Confirmed, string? Text);

public interface IConfirmService
{
    /// <summary>Pedido em aberto (lido pela janela no layout).</summary>
    ConfirmOptions? Current { get; }

    /// <summary>Há uma janela no layout para mostrar o pedido.</summary>
    bool HasHost { get; }

    event EventHandler? Changed;

    Task<ConfirmResult> Ask(ConfirmOptions options);

    Task<bool> Confirm(ConfirmOptions options);

    void RegisterHost();

    void UnregisterHost();

    void Resolve(bool confirmed, string? text = null);
}

public class ConfirmService : IConfirmService
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private TaskCompletionSource<ConfirmResult>? _pending;
    private int _hosts;

    public ConfirmOptions? Current { get; private set; }

    public bool HasHost => Volatile.Read(ref _hosts) > 0;

    public event EventHandler? Changed;

    public async Task<ConfirmResult> Ask(ConfirmOptions options)
    {
        // Uma janela por vez.
        await _lock.WaitAsync();
        try
        {
            _pending = new TaskCompletionSource<ConfirmResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            Current = options;
            Changed?.Invoke(this, EventArgs.Empty);
            return await _pending.Task;
        }
        finally
        {
            Current = null;
            _pending = null;
            Changed?.Invoke(this, EventArgs.Empty);
            _lock.Release();
        }
    }

    public async Task<bool> Confirm(ConfirmOptions options) => (await Ask(options)).Confirmed;

    public void RegisterHost() => Interlocked.Increment(ref _hosts);

    public void UnregisterHost()
    {
        if (Interlocked.Decrement(ref _hosts) <= 0)
        {
            // Layout descartado com uma janela aberta: não deixa quem pediu esperando.
            _pending?.TrySetResult(new ConfirmResult(false, null));
        }
    }

    public void Resolve(bool confirmed, string? text = null) =>
        _pending?.TrySetResult(new ConfirmResult(confirmed, text));
}
