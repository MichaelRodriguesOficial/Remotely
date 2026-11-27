namespace Remotely.Desktop.Shared.Abstractions;

/// <summary>
/// Tela preta: cobre os monitores do usuário enquanto o técnico trabalha. A cobertura não aparece na
/// imagem enviada ao técnico, que continua vendo e usando a área de trabalho normalmente.
/// </summary>
public interface IPrivacyScreen
{
    bool IsEnabled { get; }

    /// <summary>Liga ou desliga. Retorna (ligada, mensagem de erro quando não deu).</summary>
    Task<(bool Enabled, string? Error)> SetEnabled(bool enabled);
}

/// <summary>Sistemas sem suporte (Linux, macOS).</summary>
public sealed class UnsupportedPrivacyScreen : IPrivacyScreen
{
    public bool IsEnabled => false;

    public Task<(bool Enabled, string? Error)> SetEnabled(bool enabled) =>
        Task.FromResult<(bool, string?)>((false, enabled ? "A tela preta só está disponível no Windows." : null));
}
