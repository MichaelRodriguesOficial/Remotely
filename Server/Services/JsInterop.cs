using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Remotely.Server.Services;

public interface IJsInterop
{
    void AddBeforeUnloadHandler();

    void AddClassName(ElementReference element, string className);
    ValueTask Alert(string message);

    void AutoHeight();

    /// <summary>Confirmação simples (janela do painel; cai no confirm do navegador fora do layout).</summary>
    ValueTask<bool> Confirm(string message);

    /// <summary>Confirmação com título, texto do botão e estilo de ação perigosa.</summary>
    ValueTask<bool> Confirm(ConfirmOptions options);

    ValueTask<int> GetCursorIndex(ElementReference terminalInput);

    void InvokeClick(string elementId);

    void OpenWindow(string url, string target);

    /// <summary>Baixa um arquivo gerado no servidor.</summary>
    ValueTask DownloadFile(string fileName, string contentType, byte[] content);

    /// <summary>Abre a impressão de um relatório HTML (permite salvar em PDF).</summary>
    ValueTask PrintHtml(string html);

    void PreventTabOut(ElementReference terminalInput);

    ValueTask<string> Prompt(string message);
    void ScrollToElement(ElementReference element);

    void ScrollToEnd(ElementReference element);

    ValueTask<bool> SetClipboardText(string text);

    void SetStyleProperty(ElementReference element, string propertyName, string value);
    void StartDraggingY(ElementReference element, double clientY);
}

public class JsInterop : IJsInterop
{
    private readonly IJSRuntime _jsRuntime;
    private readonly IConfirmService _confirmService;

    public JsInterop(IJSRuntime jSRuntime, IConfirmService confirmService)
    {
        _jsRuntime = jSRuntime;
        _confirmService = confirmService;
    }

    public void AddBeforeUnloadHandler()
    {
        _jsRuntime.InvokeVoidAsync("addBeforeUnloadHandler");
    }

    public void AddClassName(ElementReference element, string className)
    {
        _jsRuntime.InvokeVoidAsync("addClassName", element, className);
    }

    public ValueTask Alert(string message)
    {
        return _jsRuntime.InvokeVoidAsync("invokeAlert", message);
    }

    public void AutoHeight()
    {
        _jsRuntime.InvokeVoidAsync("autoHeight");
    }

    public ValueTask<bool> Confirm(string message) =>
        Confirm(new ConfirmOptions("Confirmar", message));

    public async ValueTask<bool> Confirm(ConfirmOptions options)
    {
        if (_confirmService.HasHost)
        {
            return await _confirmService.Confirm(options);
        }

        return await _jsRuntime.InvokeAsync<bool>("invokeConfirm", $"{options.Title}\n\n{options.Message}");
    }

    public ValueTask<int> GetCursorIndex(ElementReference inputElement)
    {
        return _jsRuntime.InvokeAsync<int>("getSelectionStart", inputElement); 
    }

    public void InvokeClick(string elementId)
    {
        _jsRuntime.InvokeVoidAsync("invokeClick", elementId);
    }

    public ValueTask DownloadFile(string fileName, string contentType, byte[] content) =>
        _jsRuntime.InvokeVoidAsync("remotelyDownloadFile", fileName, contentType, Convert.ToBase64String(content));

    public ValueTask PrintHtml(string html) =>
        _jsRuntime.InvokeVoidAsync("remotelyPrintHtml", html);

    public void OpenWindow(string url, string target)
    {
        _jsRuntime.InvokeVoidAsync("openWindow", url, target);
    }

    public void PreventTabOut(ElementReference terminalInput)
    {
        _jsRuntime.InvokeVoidAsync("preventTabOut", terminalInput);
    }

    public async ValueTask<string> Prompt(string message)
    {
        if (_confirmService.HasHost)
        {
            var result = await _confirmService.Ask(new ConfirmOptions(message, string.Empty, "Salvar", Icon: "fa-solid fa-pen")
            {
                InputLabel = string.Empty
            });
            return result.Confirmed ? result.Text ?? string.Empty : string.Empty;
        }

        return await _jsRuntime.InvokeAsync<string>("invokePrompt", message);
    }

    public void ScrollToElement(ElementReference element)
    {
        _jsRuntime.InvokeVoidAsync("scrollToElement", element);
    }

    public void ScrollToEnd(ElementReference element)
    {
        _jsRuntime.InvokeVoidAsync("scrollToEnd", element);
    }

    public async ValueTask<bool> SetClipboardText(string text)
    {
        return await _jsRuntime.InvokeAsync<bool>("setClipboardText", text);
    }

    public void SetStyleProperty(ElementReference element, string propertyName, string value)
    {
        _jsRuntime.InvokeVoidAsync("setStyleProperty", element, propertyName, value);
    }
    public void StartDraggingY(ElementReference element, double clientY)
    {
        _jsRuntime.InvokeVoidAsync("startDraggingY", element, clientY);
    }
}
