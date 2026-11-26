import { ViewerApp } from "./App.js";
import { ShowToast } from "./UI.js";
export class ClipboardWatcher {
    async WatchClipboard() {
        var _a;
        if (!location.protocol.includes("https") &&
            !location.origin.includes("localhost")) {
            console.warn("Clipboard API only works in a secure context (i.e. HTTPS or localhost).");
            return;
        }
        if (!((_a = navigator.clipboard) === null || _a === void 0 ? void 0 : _a.readText)) {
            console.warn("Clipboard API not supported.");
            return;
        }
        if (this.ClipboardTimer) {
            console.log("ClipboardWatcher is already running.");
            return;
        }
        if (!this.IsEnabled()) {
            return;
        }
        try {
            // This will throw on Firefox, Safari, and possibly other browsers.
            // MDN states that they have no intention of implementing clipboard
            // permissions.
            await navigator.permissions.query({
                name: "clipboard-read"
            });
        }
        catch (ex) {
            console.error(ex);
            if (!localStorage.getItem("clipboardWarning")) {
                localStorage.setItem("clipboardWarning", "true");
                alert("A sincronização da área de transferência não está disponível neste navegador. Veja o console para detalhes.");
            }
            console.warn("New restrictions have been placed on the Clipboard API in " +
                "some browsers that make it impossible to sync the clipboard " +
                "in the background.  You can read more about this topic here: " +
                "https://developer.mozilla.org/en-US/docs/Web/API/Clipboard_API#security_considerations");
            return;
        }
        this.ClipboardTimer = window.setInterval(async () => {
            if (!document.hasFocus()) {
                return;
            }
            if (!this.IsEnabled()) {
                this.NewClipboardText = null;
                return;
            }
            if (this.NewClipboardText && navigator.clipboard.writeText) {
                try {
                    await navigator.clipboard.writeText(this.NewClipboardText);
                }
                catch (_a) {
                    return;
                }
                this.LastClipboardText = this.NewClipboardText;
                this.NewClipboardText = null;
                ShowToast("Área de transferência atualizada.");
                return;
            }
            if (ViewerApp.ViewOnlyMode) {
                return;
            }
            var newText;
            try {
                newText = await navigator.clipboard.readText();
            }
            catch (_b) {
                // Sem permissão ou documento sem foco: tenta de novo no próximo ciclo.
                return;
            }
            if (this.LastClipboardText != newText) {
                this.LastClipboardText = newText;
                ViewerApp.MessageSender.SendTextTransfer(newText, false);
            }
        }, 500);
    }
    /** Desligado pelo técnico ou sem permissão de área de transferência no perfil. */
    IsEnabled() {
        var _a;
        return ViewerApp.Settings.ClipboardSync !== false &&
            ((_a = ViewerApp.Settings.ViewerOptions) === null || _a === void 0 ? void 0 : _a.AllowClipboard) !== false;
    }
    Stop() {
        if (this.ClipboardTimer) {
            window.clearInterval(this.ClipboardTimer);
            this.ClipboardTimer = 0;
        }
    }
    SetClipboardText(text) {
        if (text == this.LastClipboardText) {
            return;
        }
        this.NewClipboardText = text;
    }
}
//# sourceMappingURL=ClipboardWatcher.js.map