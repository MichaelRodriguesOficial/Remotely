import * as UI from "./UI.js";
import { DtoType } from "./Enums/DtoType.js";
import { ViewerApp } from "./App.js";
import { ShowToast } from "./UI.js";
import { Sound } from "./Sound.js";
import {
    AudioSampleDto,
    ClipboardTextDto,
    CursorChangeDto,
    ScreenDataDto,
    ScreenSizeDto,
    FileDto,
    WindowsSessionsDto,
    DtoWrapper,
    SessionMetricsDto,
    FolderListDto,
    FileSavedDto,
    FileListDto,
    PrivacyScreenDto
} from "./Interfaces/Dtos.js";
import { ReceiveFile, HandleFileSaved, HandleClipboardFiles } from "./FileTransferService.js";
import { HandleFolderList } from "./FolderPicker.js";
import { TryComplete } from "./DtoChunker.js";
import { MessagePack } from "./Interfaces/MessagePack.js";
import { ReceiveChatMessage, ReceiveChatTyping, ReceiveChatRead } from "./ChatPanel.js";
import { ChatMessageDto, ChatSignalDto } from "./Interfaces/Dtos.js";

const MsgPack: MessagePack = window["MessagePack"];

/**
 * Grupo com "tela preta liga sozinha ao conectar": pede assim que a tela chega e as opções da
 * sessão são conhecidas (podem chegar em qualquer ordem).
 */
export function RequestAutoPrivacy() {
    const options = ViewerApp.Options;
    if (options?.PrivacyScreen == "Auto" &&
        ViewerApp.ScreenReady &&
        !ViewerApp.PrivacyAutoRequested &&
        !ViewerApp.PrivacyScreenOn &&
        !ViewerApp.ViewOnlyMode &&
        UI.PrivacyScreenButton && !UI.PrivacyScreenButton.classList.contains("d-none")) {
        ViewerApp.MessageSender.SendTogglePrivacyScreen(true);
    }
}

export class DtoMessageHandler {

    async ParseBinaryMessage(data: ArrayBuffer) {
        var wrapper = MsgPack.decode<DtoWrapper>(data);
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
                const folders = TryComplete<FolderListDto>(wrapper);
                if (folders) {
                    HandleFolderList(folders);
                }
                break;
            }
            case DtoType.ClipboardFiles: {
                const copied = TryComplete<FileListDto>(wrapper);
                if (copied) {
                    HandleClipboardFiles(copied);
                }
                break;
            }
            case DtoType.FileSaved: {
                const saved = TryComplete<FileSavedDto>(wrapper);
                if (saved) {
                    HandleFileSaved(saved);
                }
                break;
            }
            case DtoType.ScreenSize:
                this.HandleScreenSize(wrapper)
                break;
            case DtoType.WindowsSessions:
                this.HandleWindowsSessions(wrapper)
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
                const chat = TryComplete<ChatMessageDto>(wrapper);
                if (chat) {
                    ReceiveChatMessage(chat);
                }
                break;
            }
            case DtoType.ChatTyping: {
                const typing = TryComplete<ChatSignalDto>(wrapper);
                if (typing) {
                    ReceiveChatTyping(typing);
                }
                break;
            }
            case DtoType.ChatRead: {
                const read = TryComplete<ChatSignalDto>(wrapper);
                if (read) {
                    ReceiveChatRead(read);
                }
                break;
            }
            default:
                break;
        }
    }

    async HandleAudioSample(wrapper: DtoWrapper) {
        let audioSample = TryComplete<AudioSampleDto>(wrapper);

        if (!audioSample) {
            return;
        }

        await Sound.Play(audioSample.Buffer);
    }

    HandleClipboardText(wrapper: DtoWrapper) {
        let clipboardText = TryComplete<ClipboardTextDto>(wrapper);

        if (!clipboardText) {
            return;
        }

        ViewerApp.ClipboardWatcher.SetClipboardText(clipboardText.ClipboardText);
    }
    HandleCursorChange(wrapper: DtoWrapper) {
        let cursorChange = TryComplete<CursorChangeDto>(wrapper);

        if (!cursorChange) {
            return;
        }

        UI.UpdateCursor(cursorChange.ImageBytes, cursorChange.HotSpotX, cursorChange.HotSpotY, cursorChange.CssOverride);
    }
    HandleFile(wrapper: DtoWrapper) {
        let file = TryComplete<FileDto>(wrapper);

        if (!file) {
            return;
        }

        ReceiveFile(file);
    }
    HandleScreenData(wrapper: DtoWrapper) {
        let screenDataDto = TryComplete<ScreenDataDto>(wrapper);

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

    HandlePrivacyScreenState(wrapper: DtoWrapper) {
        const state = TryComplete<PrivacyScreenDto>(wrapper);
        if (!state) {
            return;
        }
        ViewerApp.PrivacyScreenOn = state.Enabled === true;
        // Resposta do computador: o pedido automático foi atendido (ou recusado), não pede de novo.
        ViewerApp.PrivacyAutoRequested = true;
        UI.SetPrivacyScreenState(ViewerApp.PrivacyScreenOn);
        UI.PrivacyScreenButton?.removeAttribute("disabled");
        if (state.Supported === false) {
            UI.PrivacyScreenButton?.classList.add("d-none");
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

    HandleScreenSize(wrapper: DtoWrapper) {
        let screenSizeDto = TryComplete<ScreenSizeDto>(wrapper);

        if (!screenSizeDto) {
            return;
        }

        UI.SetScreenSize(screenSizeDto.Width, screenSizeDto.Height);
    }

    async HandleSessionMetrics(wrapper: DtoWrapper) {
        let metricsDto = TryComplete<SessionMetricsDto>(wrapper);

        if (!metricsDto) {
            return;
        }

        UI.UpdateMetrics(metricsDto);
    }

    HandleWindowsSessions(wrapper: DtoWrapper) {
        let windowsSessionsDto = TryComplete<WindowsSessionsDto>(wrapper);

        if (!windowsSessionsDto) {
            return;
        }

        UI.UpdateWindowsSessions(windowsSessionsDto.WindowsSessions);
    }
}
