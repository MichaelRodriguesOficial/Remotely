using Remotely.Desktop.Shared.Abstractions;
using Remotely.Desktop.Shared.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Remotely.Desktop.UI.Services;

// Normally, I'd use a view model locator.  But enough view models require a factory pattern
// that I thought it more consistent to put them all here.
public interface IViewModelFactory
{
    (ChatWindowViewModel ViewModel, PipeChatChannel Channel) CreatePipeChatViewModel(string organizationName, StreamWriter streamWriter);
    ChatWindowViewModel CreateSessionChatViewModel(string technicianName);
    IFileTransferWindowViewModel CreateFileTransferWindowViewModel(IViewer viewer);
    IHostNamePromptViewModel CreateHostNamePromptViewModel();
    IPromptForAccessWindowViewModel CreatePromptForAccessViewModel(string requesterName, string organizationName, int autoAcceptSeconds = 0);
}

internal class ViewModelFactory : IViewModelFactory
{
    private readonly IServiceProvider _serviceProvider;

    public ViewModelFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public (ChatWindowViewModel ViewModel, PipeChatChannel Channel) CreatePipeChatViewModel(string organizationName, StreamWriter streamWriter)
    {
        var appState = _serviceProvider.GetRequiredService<IAppState>();
        var logger = _serviceProvider.GetRequiredService<ILogger<ChatWindowViewModel>>();
        var channel = new PipeChatChannel(streamWriter, appState.Host, logger);
        var peer = string.IsNullOrWhiteSpace(organizationName) ? "Suporte de TI" : organizationName;
        return (CreateChat(channel, peer), channel);
    }

    public ChatWindowViewModel CreateSessionChatViewModel(string technicianName)
    {
        var sessionChat = _serviceProvider.GetRequiredService<ISessionChat>();
        return CreateChat(new SessionChatChannel(sessionChat, string.Empty), technicianName);
    }

    private ChatWindowViewModel CreateChat(IChatChannel channel, string peerName)
    {
        return new ChatWindowViewModel(
            channel,
            peerName,
            string.Empty,
            _serviceProvider.GetRequiredService<IUserFiles>(),
            _serviceProvider.GetRequiredService<IBrandingProvider>(),
            _serviceProvider.GetRequiredService<IUiDispatcher>(),
            _serviceProvider.GetRequiredService<ILogger<ChatWindowViewModel>>());
    }

    public IFileTransferWindowViewModel CreateFileTransferWindowViewModel(IViewer viewer)
    {
        var brandingProvider = _serviceProvider.GetRequiredService<IBrandingProvider>();
        var dispatcher = _serviceProvider.GetRequiredService<IUiDispatcher>();
        var logger = _serviceProvider.GetRequiredService<ILogger<FileTransferWindowViewModel>>();
        var fileTransfer = _serviceProvider.GetRequiredService<IFileTransferService>();
        return new FileTransferWindowViewModel(viewer, brandingProvider, dispatcher, fileTransfer, logger);
    }

    public IPromptForAccessWindowViewModel CreatePromptForAccessViewModel(string requesterName, string organizationName, int autoAcceptSeconds = 0)
    {
        var brandingProvider = _serviceProvider.GetRequiredService<IBrandingProvider>();
        var dispatcher = _serviceProvider.GetRequiredService<IUiDispatcher>();
        var logger = _serviceProvider.GetRequiredService<ILogger<PromptForAccessWindowViewModel>>();
        return new PromptForAccessWindowViewModel(requesterName, organizationName, brandingProvider, dispatcher, logger, autoAcceptSeconds);
    }

    public IHostNamePromptViewModel CreateHostNamePromptViewModel()
    {
        var brandingProvider = _serviceProvider.GetRequiredService<IBrandingProvider>();
        var dispatcher = _serviceProvider.GetRequiredService<IUiDispatcher>();
        var logger = _serviceProvider.GetRequiredService<ILogger<HostNamePromptViewModel>>();
        return new HostNamePromptViewModel(brandingProvider, dispatcher, logger);
    }
}
