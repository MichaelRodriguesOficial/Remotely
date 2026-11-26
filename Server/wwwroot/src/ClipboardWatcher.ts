import { ViewerApp } from "./App.js";
import { ShowToast } from "./UI.js";

export class ClipboardWatcher {
    ClipboardTimer: number;
    LastClipboardText: string;
    NewClipboardText: string;

    async WatchClipboard() {
        if (!location.protocol.includes("https") &&
            !location.origin.includes("localhost")) {
            console.warn("Clipboard API only works in a secure context (i.e. HTTPS or localhost).");
            return;
        }

        if (!navigator.clipboard?.readText) {
            console.warn("Clipboard API not supported.")
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
                name: "clipboard-read" as PermissionName
            });
        }
        catch (ex) {
            console.error(ex);
            if (!localStorage.getItem("clipboardWarning")) {
                localStorage.setItem("clipboardWarning", "true");
                alert("A sincronização da área de transferência não está disponível neste navegador. Veja o console para detalhes.");
            }

            console.warn(
                "New restrictions have been placed on the Clipboard API in " +
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
                catch {
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

            var newText: string;
            try {
                newText = await navigator.clipboard.readText();
            }
            catch {
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
    IsEnabled(): boolean {
        return ViewerApp.Settings.ClipboardSync !== false &&
            ViewerApp.Settings.ViewerOptions?.AllowClipboard !== false;
    }

    Stop() {
        if (this.ClipboardTimer) {
            window.clearInterval(this.ClipboardTimer);
            this.ClipboardTimer = 0;
        }
    }

    SetClipboardText(text: string) {
        if (text == this.LastClipboardText) {
            return;
        }

        this.NewClipboardText = text;
    }
}
