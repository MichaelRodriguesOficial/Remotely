import { ViewerApp } from "./App.js";
import { ShowToast } from "./UI.js";

export class ClipboardWatcher {
    ClipboardTimer: number;
    LastClipboardText: string;
    NewClipboardText: string;

    /**
     * Copiar e colar automático. Daqui para lá: o Ctrl+V na tela remota (evento "paste", sem pedir
     * permissão de leitura ao navegador). De lá para cá: o texto copiado no computador remoto chega
     * pelo servidor e é colocado na área de transferência daqui quando esta janela está em foco.
     */
    async WatchClipboard() {
        if (!window.isSecureContext || !navigator.clipboard?.writeText) {
            console.warn("Clipboard API only works in a secure context (i.e. HTTPS or localhost).");
            return;
        }

        if (this.ClipboardTimer) {
            return;
        }

        if (!this.IsEnabled()) {
            return;
        }

        this.ClipboardTimer = window.setInterval(async () => {
            if (!document.hasFocus() || !this.NewClipboardText) {
                return;
            }

            if (!this.IsEnabled()) {
                this.NewClipboardText = null;
                return;
            }

            try {
                await navigator.clipboard.writeText(this.NewClipboardText);
            }
            catch {
                return;
            }
            this.LastClipboardText = this.NewClipboardText;
            this.NewClipboardText = null;
            ShowToast("Texto copiado no computador remoto: já pode colar aqui.");
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
