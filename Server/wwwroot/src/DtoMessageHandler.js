import * as UI from "./UI.js";
import { DtoType } from "./Enums/DtoType.js";
import { ViewerApp } from "./App.js";
import { ShowToast } from "./UI.js";
import { Sound } from "./Sound.js";
import { ReceiveFile, HandleFileSaved, HandleClipboardFiles } from "./FileTransferService.js";
import { HandleFolderList } from "./FolderPicker.js";
import { TryComplete } from "./DtoChunker.js";
import { ReceiveChatMessage, ReceiveChatTyping, ReceiveChatRead } from "./ChatPanel.js";
const MsgPack = window["MessagePack"];
/**
 * Grupo com "tela preta liga sozinha ao conectar": pede assim que a tela chega e as opções da
 * sessão são conhecidas (podem chegar em qualquer ordem).
 */
export function RequestAutoPrivacy() {
    const options = ViewerApp.Options;
    if ((options === null || options === void 0 ? void 0 : options.PrivacyScreen) == "Auto" &&
        ViewerApp.ScreenReady &&
        !ViewerApp.PrivacyAutoRequested &&
        !ViewerApp.PrivacyScreenOn &&
        !ViewerApp.ViewOnlyMode &&
        UI.PrivacyScreenButton && !UI.PrivacyScreenButton.classList.contains("d-none")) {
        ViewerApp.MessageSender.SendTogglePrivacyScreen(true);
    }
}
export class DtoMessageHandler {
    async ParseBinaryMessage(data) {
        var wrapper = MsgPack.decode(data);
        switch (wrapper.DtoType) {
            case DtoType.AudioSample:
                await this.HandleAudioSample(wrapper);
                break;
            case DtoType.ClipboardText:
                this.HandleClipboardText(wrapper);
                break;
            case DtoType.CursorChange:
                this.HandleCursorChange(wrapper);
                break;
            case DtoType.ScreenData:
                this.HandleScreenData(wrapper);
                break;
            case DtoType.FolderList: {
                const folders = TryComplete(wrapper);
                if (folders) {
                    HandleFolderList(folders);
                }
                break;
            }
            case DtoType.ClipboardFiles: {
                const copied = TryComplete(wrapper);
                if (copied) {
                    HandleClipboardFiles(copied);
                }
                break;
            }
            case DtoType.FileSaved: {
                const saved = TryComplete(wrapper);
                if (saved) {
                    HandleFileSaved(saved);
                }
                break;
            }
            case DtoType.ScreenSize:
                this.HandleScreenSize(wrapper);
                break;
            case DtoType.WindowsSessions:
                this.HandleWindowsSessions(wrapper);
                break;
            case DtoType.File:
                this.HandleFile(wrapper);
                break;
            case DtoType.SessionMetrics:
                await this.HandleSessionMetrics(wrapper);
                break;
            case DtoType.PrivacyScreenState:
                this.HandlePrivacyScreenState(wrapper);
                break;
            case DtoType.ChatMessage: {
                const chat = TryComplete(wrapper);
                if (chat) {
                    ReceiveChatMessage(chat);
                }
                break;
            }
            case DtoType.ChatTyping: {
                const typing = TryComplete(wrapper);
                if (typing) {
                    ReceiveChatTyping(typing);
                }
                break;
            }
            case DtoType.ChatRead: {
                const read = TryComplete(wrapper);
                if (read) {
                    ReceiveChatRead(read);
                }
                break;
            }
            default:
                break;
        }
    }
    async HandleAudioSample(wrapper) {
        let audioSample = TryComplete(wrapper);
        if (!audioSample) {
            return;
        }
        await Sound.Play(audioSample.Buffer);
    }
    HandleClipboardText(wrapper) {
        let clipboardText = TryComplete(wrapper);
        if (!clipboardText) {
            return;
        }
        ViewerApp.ClipboardWatcher.SetClipboardText(clipboardText.ClipboardText);
    }
    HandleCursorChange(wrapper) {
        let cursorChange = TryComplete(wrapper);
        if (!cursorChange) {
            return;
        }
        UI.UpdateCursor(cursorChange.ImageBytes, cursorChange.HotSpotX, cursorChange.HotSpotY, cursorChange.CssOverride);
    }
    HandleFile(wrapper) {
        let file = TryComplete(wrapper);
        if (!file) {
            return;
        }
        ReceiveFile(file);
    }
    HandleScreenData(wrapper) {
        let screenDataDto = TryComplete(wrapper);
        if (!screenDataDto) {
            return;
        }
        document.title = `${screenDataDto.MachineName} - Sessão de controle remoto`;
        UI.ToggleConnectUI(false);
        UI.HideWaitOverlay();
        UI.SetSessionIdentity(screenDataDto.MachineName);
        UI.SetScreenSize(screenDataDto.ScreenWidth, screenDataDto.ScreenHeight);
        UI.UpdateDisplays(screenDataDto.SelectedDisplay, screenDataDto.DisplayNames);
        ViewerApp.ScreenReady = true;
        RequestAutoPrivacy();
    }
    HandlePrivacyScreenState(wrapper) {
        var _a, _b;
        const state = TryComplete(wrapper);
        if (!state) {
            return;
        }
        ViewerApp.PrivacyScreenOn = state.Enabled === true;
        // Resposta do computador: o pedido automático foi atendido (ou recusado), não pede de novo.
        ViewerApp.PrivacyAutoRequested = true;
        UI.SetPrivacyScreenState(ViewerApp.PrivacyScreenOn);
        (_a = UI.PrivacyScreenButton) === null || _a === void 0 ? void 0 : _a.removeAttribute("disabled");
        if (state.Supported === false) {
            (_b = UI.PrivacyScreenButton) === null || _b === void 0 ? void 0 : _b.classList.add("d-none");
        }
        if (state.Message) {
            ShowToast(state.Message, 7000);
        }
        else {
            ShowToast(state.Enabled
                ? "Tela preta ligada: o usuário não vê a tela e o teclado e o mouse dele estão bloqueados."
                : "Tela preta desligada.");
        }
    }
    HandleScreenSize(wrapper) {
        let screenSizeDto = TryComplete(wrapper);
        if (!screenSizeDto) {
            return;
        }
        UI.SetScreenSize(screenSizeDto.Width, screenSizeDto.Height);
    }
    async HandleSessionMetrics(wrapper) {
        let metricsDto = TryComplete(wrapper);
        if (!metricsDto) {
            return;
        }
        UI.UpdateMetrics(metricsDto);
    }
    HandleWindowsSessions(wrapper) {
        let windowsSessionsDto = TryComplete(wrapper);
        if (!windowsSessionsDto) {
            return;
        }
        UI.UpdateWindowsSessions(windowsSessionsDto.WindowsSessions);
    }
}
//# sourceMappingURL=DtoMessageHandler.js.map