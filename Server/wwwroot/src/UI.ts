import { ViewerApp } from "./App.js";
import { ConvertUInt8ArrayToBase64 } from "./Utilities.js";
import { WindowsSession } from "./Models/WindowsSession.js";
import { WindowsSessionType } from "./Enums/WindowsSessionType.js";
import { RemoteControlMode } from "./Enums/RemoteControlMode.js";
import { SessionMetricsDto } from "./Interfaces/Dtos.js";

export var AudioButton = document.getElementById("audioButton") as HTMLButtonElement;
export var MenuButton = document.getElementById("menuButton") as HTMLButtonElement;
export var MenuFrame = document.getElementById("menuFrame") as HTMLDivElement;
export var SessionIDInput = document.getElementById("sessionIDInput") as HTMLInputElement;
export var ConnectButton = document.getElementById("connectButton") as HTMLButtonElement;
export var RequesterNameInput = document.getElementById("nameInput") as HTMLInputElement;
export var StatusMessage = document.getElementById("statusMessage") as HTMLDivElement;
export var ScreenViewer = document.getElementById("screenViewer") as HTMLCanvasElement;
export var ScreenViewerWrapper = document.getElementById("screenViewerWrapper") as HTMLDivElement;
export var Screen2DContext = ScreenViewer ? ScreenViewer.getContext("2d") : null;
export var PopupMenus = document.querySelectorAll(".popup-menu");
export var ConnectBox = document.getElementById("connectBox") as HTMLDivElement;
export var ConnectHeader = document.getElementById("connectHeader") as HTMLHeadingElement;
export var ConnectForm = document.getElementById("connectForm") as HTMLFormElement;
export var ScreenSelectMenu = document.getElementById("screenSelectMenu") as HTMLDivElement;
export var ChangeScreenButton = document.getElementById("changeScreenButton") as HTMLButtonElement;
export var FitToScreenButton = document.getElementById("fitToScreenButton") as HTMLButtonElement;
export var BlockInputButton = document.getElementById("blockInputButton") as HTMLButtonElement;
export var DisconnectButton = document.getElementById("disconnectButton") as HTMLButtonElement;
export var FileTransferInput = document.getElementById("fileTransferInput") as HTMLInputElement;
export var FileTransferProgress = document.getElementById("fileTransferProgress") as HTMLProgressElement;
export var FileTransferNameSpan = document.getElementById("fileTransferNameSpan") as HTMLSpanElement;
export var KeyboardButton = document.getElementById("keyboardButton") as HTMLButtonElement;
export var InviteButton = document.getElementById("inviteButton") as HTMLButtonElement;
export var FileTransferButton = document.getElementById("fileTransferButton") as HTMLButtonElement;
export var FileTransferMenu = document.getElementById("fileTransferMenu") as HTMLDivElement;
export var FileUploadButtton = document.getElementById("fileUploadButton") as HTMLButtonElement;
export var FileDownloadButton = document.getElementById("fileDownloadButton") as HTMLButtonElement;
export var CtrlAltDelButton = document.getElementById("ctrlAltDelButton") as HTMLButtonElement;
export var TouchKeyboardInput = document.getElementById("touchKeyboardInput") as HTMLTextAreaElement;
export var ClipboardTransferMenu = document.getElementById("clipboardTransferMenu") as HTMLDivElement;
export var ClipboardTransferButton = document.getElementById("clipboardTransferButton") as HTMLButtonElement;
export var TypeClipboardButton = document.getElementById("typeClipboardButton") as HTMLButtonElement;
export var ConnectionP2PIcon = document.getElementById("connectionP2PIcon") as HTMLElement;
export var ConnectionRelayedIcon = document.getElementById("connectionRelayedIcon") as HTMLElement;
export var WindowsSessionSelect = document.getElementById("windowsSessionSelect") as HTMLSelectElement;
export var ViewOnlyButton = document.getElementById("viewOnlyButton") as HTMLButtonElement;
export var FullScreenButton = document.getElementById("fullScreenButton") as HTMLButtonElement;
export var ToastsWrapper = document.getElementById("toastsWrapper") as HTMLDivElement;
export var MbpsDiv = document.getElementById("mbpsDiv") as HTMLDivElement;
export var FpsDiv = document.getElementById("fpsDiv") as HTMLDivElement;
export var LatencyDiv = document.getElementById("latencyDiv") as HTMLDivElement;
export var GpuDiv = document.getElementById("gpuAcceleratedDiv") as HTMLDivElement;
export var WorkAreaGrid = document.getElementById("workAreaGrid") as HTMLDivElement;
export var BackgroundLayers = document.getElementById("backgroundLayers") as HTMLDivElement;
export var ExtrasMenu = document.getElementById("extrasMenu") as HTMLDivElement;
export var ExtrasMenuButton = document.getElementById("extrasMenuButton") as HTMLButtonElement;
export var WindowsSessionMenu = document.getElementById("windowsSessionMenu") as HTMLDivElement;
export var WindowsSessionMenuButton = document.getElementById("windowsSessionMenuButton") as HTMLButtonElement;
export var MetricsButton = document.getElementById("metricsButton") as HTMLButtonElement;
export var MetricsFrame = document.getElementById("metricsFrame") as HTMLDivElement;
export var BetaPillPullDown = document.getElementById("betaPillPullDown") as HTMLDivElement;
export var MachineNameLabel = document.getElementById("machineNameLabel") as HTMLElement;
export var SessionTimerLabel = document.getElementById("sessionTimerLabel") as HTMLElement;
export var MonitorButtons = document.getElementById("monitorButtons") as HTMLDivElement;
export var MonitorGroup = document.getElementById("monitorGroup") as HTMLDivElement;
export var PanelButton = document.getElementById("panelButton") as HTMLButtonElement;
export var SidePanel = document.getElementById("sidePanel") as HTMLElement;
export var SidePanelFrame = document.getElementById("sidePanelFrame") as HTMLIFrameElement;
export var SidePanelClose = document.getElementById("sidePanelClose") as HTMLButtonElement;
export var SidePanelPopout = document.getElementById("sidePanelPopout") as HTMLAnchorElement;
export var RestartButton = document.getElementById("restartButton") as HTMLButtonElement;
export var ClipboardSyncButton = document.getElementById("clipboardSyncButton") as HTMLButtonElement;
export var SendClipboardButton = document.getElementById("sendClipboardButton") as HTMLButtonElement;
export var WaitOverlay = document.getElementById("waitOverlay") as HTMLDivElement;
export var WaitOverlayTitle = document.getElementById("waitOverlayTitle") as HTMLElement;
export var WaitOverlayText = document.getElementById("waitOverlayText") as HTMLElement;
export var WaitOverlayTimer = document.getElementById("waitOverlayTimer") as HTMLElement;
export var WaitOverlayCancel = document.getElementById("waitOverlayCancel") as HTMLButtonElement;
export var RestartSafeButton = document.getElementById("restartSafeButton") as HTMLButtonElement;
export var SessionEndedOverlay = document.getElementById("sessionEndedOverlay") as HTMLDivElement;
export var SessionEndedText = document.getElementById("sessionEndedText") as HTMLElement;
export var SessionEndedClose = document.getElementById("sessionEndedClose") as HTMLButtonElement;

/** Fim da sessão: fecha a aba se o técnico encerrou; senão (ou se o navegador não deixar) mostra o aviso. */
export function ShowSessionEnded(reason: string, tryClose: boolean) {
    if (tryClose) {
        window.close();
    }
    // Se a aba não pôde ser fechada (aberta por um link fora do painel), o aviso aparece.
    window.setTimeout(() => {
        if (!SessionEndedOverlay) {
            return;
        }
        SessionEndedText.textContent = reason || "A sessão de controle remoto terminou.";
        SessionEndedOverlay.classList.remove("d-none");
        StatusMessage.classList.add("d-none");
    }, tryClose ? 400 : 0);
}

/** Janela para escrever ou colar um texto. Retorna null se cancelar. */
export function PromptText(title: string, message: string, note: string, okText: string, initial: string): Promise<string | null> {
    return new Promise(resolve => {
        const backdrop = document.createElement("div");
        backdrop.className = "modal-confirm";
        const card = document.createElement("div");
        card.className = "confirm-card";
        card.setAttribute("role", "dialog");
        card.setAttribute("aria-modal", "true");
        const heading = document.createElement("h3");
        heading.textContent = title;
        const body = document.createElement("p");
        body.textContent = message;
        const input = document.createElement("textarea");
        input.value = initial || "";
        input.spellcheck = false;
        input.setAttribute("aria-label", title);
        const hint = document.createElement("div");
        hint.className = "confirm-note";
        hint.textContent = note;
        const actions = document.createElement("div");
        actions.className = "confirm-actions";
        const cancel = document.createElement("button");
        cancel.textContent = "Cancelar";
        const ok = document.createElement("button");
        ok.className = "confirm-ok";
        ok.textContent = okText;
        actions.append(cancel, ok);
        card.append(heading, body, input, hint, actions);
        backdrop.appendChild(card);
        document.body.appendChild(backdrop);
        input.focus();
        input.select();

        const finish = (value: string | null) => {
            backdrop.remove();
            resolve(value);
        };
        ok.onclick = () => finish(input.value ? input.value : null);
        cancel.onclick = () => finish(null);
        card.onkeydown = ev => {
            ev.stopPropagation();
            if (ev.key == "Escape") {
                finish(null);
            }
            else if (ev.key == "Enter" && (ev.ctrlKey || ev.metaKey)) {
                finish(input.value ? input.value : null);
            }
        };
        card.onkeyup = ev => ev.stopPropagation();
    });
}

function FormatElapsed(ms: number): string {
    const total = Math.max(0, Math.floor(ms / 1000));
    const h = Math.floor(total / 3600);
    const m = Math.floor((total % 3600) / 60);
    const s = total % 60;
    const pad = (n: number) => n.toString().padStart(2, "0");
    return h > 0 ? `${h}:${pad(m)}:${pad(s)}` : `${pad(m)}:${pad(s)}`;
}

var sessionStartedAt = 0;
var sessionTimer = -1;

/** Mostra o nome do computador e começa a contar o tempo de sessão (uma vez por página). */
export function SetSessionIdentity(machineName: string) {
    if (MachineNameLabel && machineName) {
        MachineNameLabel.textContent = machineName;
        MachineNameLabel.title = machineName;
    }

    if (sessionStartedAt == 0) {
        sessionStartedAt = Date.now();
        sessionTimer = window.setInterval(() => {
            if (SessionTimerLabel) {
                SessionTimerLabel.textContent = `Conectado há ${FormatElapsed(Date.now() - sessionStartedAt)}`;
            }
        }, 1000);
        if (SessionTimerLabel) {
            SessionTimerLabel.textContent = "Conectado agora";
        }
    }
}

var waitStartedAt = 0;
var waitTimer = -1;

/** Tela de espera (ex.: reiniciando o computador). Some quando a imagem volta. */
export function ShowWaitOverlay(title: string, text: string) {
    if (!WaitOverlay) {
        return;
    }
    WaitOverlayTitle.textContent = title;
    WaitOverlayText.textContent = text;
    WaitOverlay.classList.remove("d-none");
    if (waitTimer < 0) {
        waitStartedAt = Date.now();
        WaitOverlayTimer.textContent = "00:00";
        waitTimer = window.setInterval(() => {
            WaitOverlayTimer.textContent = FormatElapsed(Date.now() - waitStartedAt);
        }, 1000);
    }
}

export function HideWaitOverlay() {
    if (!WaitOverlay) {
        return;
    }
    WaitOverlay.classList.add("d-none");
    if (waitTimer >= 0) {
        window.clearInterval(waitTimer);
        waitTimer = -1;
    }
}

export function IsWaitOverlayShown(): boolean {
    return !!WaitOverlay && !WaitOverlay.classList.contains("d-none");
}

/** Confirmação na própria página (sem o confirm() do navegador, que trava o visualizador). */
export function Confirm(title: string, message: string, okText: string): Promise<boolean> {
    return new Promise(resolve => {
        const backdrop = document.createElement("div");
        backdrop.className = "modal-confirm";
        const card = document.createElement("div");
        card.className = "confirm-card";
        card.setAttribute("role", "dialog");
        card.setAttribute("aria-modal", "true");
        const heading = document.createElement("h3");
        heading.textContent = title;
        const body = document.createElement("p");
        body.textContent = message;
        const actions = document.createElement("div");
        actions.className = "confirm-actions";
        const cancel = document.createElement("button");
        cancel.textContent = "Cancelar";
        const ok = document.createElement("button");
        ok.className = "confirm-ok";
        ok.textContent = okText;
        actions.append(cancel, ok);
        card.append(heading, body, actions);
        backdrop.appendChild(card);
        document.body.appendChild(backdrop);
        ok.focus();

        const finish = (value: boolean) => {
            backdrop.remove();
            resolve(value);
        };
        ok.onclick = () => finish(true);
        cancel.onclick = () => finish(false);
        backdrop.onclick = ev => {
            if (ev.target == backdrop) {
                finish(false);
            }
        };
        card.onkeydown = ev => {
            ev.stopPropagation();
            if (ev.key == "Escape") {
                finish(false);
            }
        };
        card.onkeyup = ev => ev.stopPropagation();
    });
}

export function CloseAllPopupMenus(exceptMenuId: string) {
    PopupMenus.forEach(x => {
        if (x.id != exceptMenuId) {
            x.classList.remove("open");
        }
    })
}

export function Prompt(promptMessage: string): Promise<string> {
    return new Promise((resolve, reject) => {
        var modalDiv = document.createElement("div");
        modalDiv.classList.add("modal-prompt");

        var messageDiv = document.createElement("div");
        messageDiv.textContent = promptMessage;

        var responseInput = document.createElement("input");

        var buttonsDiv = document.createElement("div");
        buttonsDiv.classList.add("buttons-footer");

        var cancelButton = document.createElement("button");
        cancelButton.innerHTML = "Cancelar";

        var okButton = document.createElement("button");
        okButton.innerHTML = "OK";

        buttonsDiv.appendChild(okButton);
        buttonsDiv.appendChild(cancelButton);
        modalDiv.appendChild(messageDiv);
        modalDiv.appendChild(responseInput);
        modalDiv.appendChild(buttonsDiv);

        document.body.appendChild(modalDiv);

        okButton.onclick = () => {
            modalDiv.remove();
            resolve(responseInput.value);
        }

        cancelButton.onclick = () => {
            modalDiv.remove();
            resolve(null);
        }
    });
}


export function SetScreenSize(width: number, height: number) {
    ScreenViewer.width = width;
    ScreenViewer.height = height;
    Screen2DContext.clearRect(0, 0, width, height);
}

export function SetStatusMessage(message: string) {
    StatusMessage.innerText = message;
}

export function ShowToast(message: string, durationMs: number = 5000) {
    var messageDiv = document.createElement("div");
    messageDiv.classList.add("toast-message");
    if (durationMs > 5000) {
        messageDiv.style.animationDelay = `${(durationMs - 3000) / 1000}s`;
    }
    // Messages can originate from the remote device (e.g. file names),
    // so never render them as HTML.
    messageDiv.textContent = message;
    ToastsWrapper.appendChild(messageDiv);
    window.setTimeout(() => {
        messageDiv.remove();
    }, Math.max(durationMs, 5000));
}


export function ToggleConnectUI(shown: boolean) {
    if (shown) {
        ConnectButton.innerText = "Conectar";
        Screen2DContext.clearRect(0, 0, ScreenViewer.width, ScreenViewer.height);
        ScreenViewerWrapper.setAttribute("hidden", "hidden");
        if (ViewerApp.Mode == RemoteControlMode.Attended) {
            ConnectBox.style.removeProperty("display");
            ConnectHeader.style.removeProperty("display");
        }
        BlockInputButton.classList.remove("toggled");
        AudioButton.classList.remove("toggled");
        WorkAreaGrid.style.display = "none";
        BackgroundLayers.classList.remove("d-none");
        CloseAllPopupMenus(null);
    }
    else {
        ConnectBox.style.display = "none";
        ConnectHeader.style.display = "none";
        ScreenViewerWrapper.removeAttribute("hidden");
        StatusMessage.innerHTML = "";
        WorkAreaGrid.style.removeProperty("display");
        BackgroundLayers.classList.add("d-none");
    }

    ConnectButton.disabled = !ViewerApp.RequesterName || !ViewerApp.SessionId;
}

export function UpdateCursor(imageBytes: Uint8Array, hotSpotX: number, hotSpotY: number, cssOverride: string) {
    if (cssOverride) {
        ScreenViewer.style.cursor = cssOverride;
    }
    else if (imageBytes.byteLength == 0) {
        ScreenViewer.style.cursor = "default";
    }
    else {
        var base64 = ConvertUInt8ArrayToBase64(imageBytes);
        ScreenViewer.style.cursor = `url('data:image/png;base64,${base64}') ${hotSpotX} ${hotSpotY}, default`;
    }
}

export function UpdateDisplays(selectedDisplay: string, displayNames: string[]) {
    ScreenSelectMenu.innerHTML = "";
    if (MonitorButtons) {
        MonitorButtons.innerHTML = "";
        const groupIcon = document.createElement("span");
        groupIcon.className = "rc-group-icon";
        groupIcon.title = "Monitores do computador remoto";
        groupIcon.innerHTML = '<i class="fas fa-desktop"></i>';
        MonitorButtons.appendChild(groupIcon);
    }

    const select = (displayName: string) => {
        ViewerApp.MessageSender.SendSelectScreen(displayName);
        UpdateDisplays(displayName, displayNames);
    };

    for (let i = 0; i < displayNames.length; i++) {
        const isSelected = displayNames[i] == selectedDisplay;

        var button = document.createElement("button");
        const icon = document.createElement("i");
        icon.className = "fas fa-desktop";
        button.append(icon, document.createTextNode(` Monitor ${i + 1}`));
        if (isSelected) {
            button.classList.add("toggled");
        }
        ScreenSelectMenu.appendChild(button);
        button.onclick = () => {
            ScreenSelectMenu.classList.remove("open");
            select(displayNames[i]);
        };

        if (MonitorButtons) {
            const inline = document.createElement("button");
            inline.className = "rc-btn" + (isSelected ? " toggled" : "");
            inline.textContent = `${i + 1}`;
            inline.title = `Ver o monitor ${i + 1}`;
            inline.setAttribute("aria-pressed", isSelected ? "true" : "false");
            inline.onclick = ev => {
                ev.stopPropagation();
                select(displayNames[i]);
            };
            MonitorButtons.appendChild(inline);
        }
    }

    // Até 6 monitores: botões numerados direto na barra. Mais que isso: menu.
    // Um monitor só: nada a escolher.
    const inlineMode = displayNames.length > 1 && displayNames.length <= 6;
    if (MonitorGroup) {
        MonitorGroup.classList.toggle("d-none", displayNames.length <= 1);
    }
    if (MonitorButtons) {
        MonitorButtons.classList.toggle("d-none", !inlineMode);
    }
    ChangeScreenButton.classList.toggle("d-none", inlineMode);
    if (inlineMode) {
        ChangeScreenButton.title = "Escolher o monitor do computador remoto.";
    }
}


export function UpdateMetrics(metricsDto: SessionMetricsDto) {
    FpsDiv.innerHTML = metricsDto.Fps.toFixed(0);
    MbpsDiv.innerHTML = metricsDto.Mbps.toFixed(2);
    LatencyDiv.innerHTML = `${metricsDto.RoundTripLatency.toFixed(2)}ms`;
    GpuDiv.innerHTML = metricsDto.IsGpuAccelerated ? "Ativa" : "Indisponível";
}

export function UpdateWindowsSessions(windowsSessions: Array<WindowsSession>) {
    while (WindowsSessionSelect.options.length > 0) {
        WindowsSessionSelect.options.remove(0);
    }

    WindowsSessionSelect.options.add(document.createElement("option"));

    windowsSessions.forEach(x => {
        var sessionType = "";

        if (typeof x.Type == "number") {
            sessionType = x.Type == WindowsSessionType.Console ? "Console" : "RDP";
        }
        else {
            sessionType = x.Type;
        }

        var option = document.createElement("option");
        option.value = String(x.ID);
        option.text = `${sessionType} (ID: ${x.ID} | User: ${x.Username})`;
        option.title = `Sessão ${sessionType} (ID: ${x.ID} | Usuário: ${x.Username})`;
        WindowsSessionSelect.options.add(option);
    });
}
