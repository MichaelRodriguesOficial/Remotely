import { ViewerApp } from "./App.js";
import { ConvertUInt8ArrayToBase64 } from "./Utilities.js";
import { WindowsSessionType } from "./Enums/WindowsSessionType.js";
import { RemoteControlMode } from "./Enums/RemoteControlMode.js";
export var AudioButton = document.getElementById("audioButton");
export var MenuButton = document.getElementById("menuButton");
export var MenuFrame = document.getElementById("menuFrame");
export var SessionIDInput = document.getElementById("sessionIDInput");
export var ConnectButton = document.getElementById("connectButton");
export var RequesterNameInput = document.getElementById("nameInput");
export var StatusMessage = document.getElementById("statusMessage");
export var ScreenViewer = document.getElementById("screenViewer");
export var ScreenViewerWrapper = document.getElementById("screenViewerWrapper");
export var Screen2DContext = ScreenViewer ? ScreenViewer.getContext("2d") : null;
export var PopupMenus = document.querySelectorAll(".popup-menu");
export var ConnectBox = document.getElementById("connectBox");
export var ConnectHeader = document.getElementById("connectHeader");
export var ConnectForm = document.getElementById("connectForm");
export var ScreenSelectMenu = document.getElementById("screenSelectMenu");
export var ChangeScreenButton = document.getElementById("changeScreenButton");
export var FitToScreenButton = document.getElementById("fitToScreenButton");
export var BlockInputButton = document.getElementById("blockInputButton");
export var DisconnectButton = document.getElementById("disconnectButton");
export var FileTransferInput = document.getElementById("fileTransferInput");
export var FileTransferProgress = document.getElementById("fileTransferProgress");
export var FileTransferNameSpan = document.getElementById("fileTransferNameSpan");
export var KeyboardButton = document.getElementById("keyboardButton");
export var InviteButton = document.getElementById("inviteButton");
export var FileTransferButton = document.getElementById("fileTransferButton");
export var FileTransferMenu = document.getElementById("fileTransferMenu");
export var FileUploadButtton = document.getElementById("fileUploadButton");
export var FileDownloadButton = document.getElementById("fileDownloadButton");
export var CtrlAltDelButton = document.getElementById("ctrlAltDelButton");
export var TouchKeyboardInput = document.getElementById("touchKeyboardInput");
export var ClipboardTransferMenu = document.getElementById("clipboardTransferMenu");
export var ClipboardTransferButton = document.getElementById("clipboardTransferButton");
export var TypeClipboardButton = document.getElementById("typeClipboardButton");
export var ConnectionP2PIcon = document.getElementById("connectionP2PIcon");
export var ConnectionRelayedIcon = document.getElementById("connectionRelayedIcon");
export var WindowsSessionSelect = document.getElementById("windowsSessionSelect");
export var ViewOnlyButton = document.getElementById("viewOnlyButton");
export var FullScreenButton = document.getElementById("fullScreenButton");
export var ToastsWrapper = document.getElementById("toastsWrapper");
export var MbpsDiv = document.getElementById("mbpsDiv");
export var FpsDiv = document.getElementById("fpsDiv");
export var LatencyDiv = document.getElementById("latencyDiv");
export var GpuDiv = document.getElementById("gpuAcceleratedDiv");
export var WorkAreaGrid = document.getElementById("workAreaGrid");
export var BackgroundLayers = document.getElementById("backgroundLayers");
export var ExtrasMenu = document.getElementById("extrasMenu");
export var ExtrasMenuButton = document.getElementById("extrasMenuButton");
export var WindowsSessionMenu = document.getElementById("windowsSessionMenu");
export var WindowsSessionMenuButton = document.getElementById("windowsSessionMenuButton");
export var MetricsButton = document.getElementById("metricsButton");
export var MetricsFrame = document.getElementById("metricsFrame");
export var BetaPillPullDown = document.getElementById("betaPillPullDown");
export var MachineNameLabel = document.getElementById("machineNameLabel");
export var SessionTimerLabel = document.getElementById("sessionTimerLabel");
export var MonitorButtons = document.getElementById("monitorButtons");
export var MonitorGroup = document.getElementById("monitorGroup");
export var PanelButton = document.getElementById("panelButton");
export var SidePanel = document.getElementById("sidePanel");
export var SidePanelFrame = document.getElementById("sidePanelFrame");
export var SidePanelClose = document.getElementById("sidePanelClose");
export var SidePanelPopout = document.getElementById("sidePanelPopout");
export var RestartButton = document.getElementById("restartButton");
export var ClipboardSyncButton = document.getElementById("clipboardSyncButton");
export var SendClipboardButton = document.getElementById("sendClipboardButton");
export var WaitOverlay = document.getElementById("waitOverlay");
export var WaitOverlayTitle = document.getElementById("waitOverlayTitle");
export var WaitOverlayText = document.getElementById("waitOverlayText");
export var WaitOverlayTimer = document.getElementById("waitOverlayTimer");
export var WaitOverlayCancel = document.getElementById("waitOverlayCancel");
export var RestartSafeButton = document.getElementById("restartSafeButton");
export var SessionEndedOverlay = document.getElementById("sessionEndedOverlay");
export var SessionEndedText = document.getElementById("sessionEndedText");
export var SessionEndedClose = document.getElementById("sessionEndedClose");
/** Fim da sessão: fecha a aba se o técnico encerrou; senão (ou se o navegador não deixar) mostra o aviso. */
export function ShowSessionEnded(reason, tryClose) {
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
export function PromptText(title, message, note, okText, initial) {
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
        const finish = (value) => {
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
function FormatElapsed(ms) {
    const total = Math.max(0, Math.floor(ms / 1000));
    const h = Math.floor(total / 3600);
    const m = Math.floor((total % 3600) / 60);
    const s = total % 60;
    const pad = (n) => n.toString().padStart(2, "0");
    return h > 0 ? `${h}:${pad(m)}:${pad(s)}` : `${pad(m)}:${pad(s)}`;
}
var sessionStartedAt = 0;
var sessionTimer = -1;
/** Mostra o nome do computador e começa a contar o tempo de sessão (uma vez por página). */
export function SetSessionIdentity(machineName) {
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
export function ShowWaitOverlay(title, text) {
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
export function IsWaitOverlayShown() {
    return !!WaitOverlay && !WaitOverlay.classList.contains("d-none");
}
/** Confirmação na própria página (sem o confirm() do navegador, que trava o visualizador). */
export function Confirm(title, message, okText) {
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
        const finish = (value) => {
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
export function CloseAllPopupMenus(exceptMenuId) {
    PopupMenus.forEach(x => {
        if (x.id != exceptMenuId) {
            x.classList.remove("open");
        }
    });
}
export function Prompt(promptMessage) {
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
        };
        cancelButton.onclick = () => {
            modalDiv.remove();
            resolve(null);
        };
    });
}
export function SetScreenSize(width, height) {
    ScreenViewer.width = width;
    ScreenViewer.height = height;
    Screen2DContext.clearRect(0, 0, width, height);
}
export function SetStatusMessage(message) {
    StatusMessage.innerText = message;
}
export function ShowToast(message, durationMs = 5000) {
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
export function ToggleConnectUI(shown) {
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
export function UpdateCursor(imageBytes, hotSpotX, hotSpotY, cssOverride) {
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
export function UpdateDisplays(selectedDisplay, displayNames) {
    ScreenSelectMenu.innerHTML = "";
    if (MonitorButtons) {
        MonitorButtons.innerHTML = "";
        const groupIcon = document.createElement("span");
        groupIcon.className = "rc-group-icon";
        groupIcon.title = "Monitores do computador remoto";
        groupIcon.innerHTML = '<i class="fas fa-desktop"></i>';
        MonitorButtons.appendChild(groupIcon);
    }
    const select = (displayName) => {
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
export function UpdateMetrics(metricsDto) {
    FpsDiv.innerHTML = metricsDto.Fps.toFixed(0);
    MbpsDiv.innerHTML = metricsDto.Mbps.toFixed(2);
    LatencyDiv.innerHTML = `${metricsDto.RoundTripLatency.toFixed(2)}ms`;
    GpuDiv.innerHTML = metricsDto.IsGpuAccelerated ? "Ativa" : "Indisponível";
}
export function UpdateWindowsSessions(windowsSessions) {
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
//# sourceMappingURL=UI.js.map