import {
    AudioButton,
    ChangeScreenButton,
    PopupMenus,
    ScreenSelectMenu,
    ClipboardTransferButton,
    ClipboardTransferMenu,
    TypeClipboardButton,
    ConnectButton,
    CtrlAltDelButton,
    DisconnectButton,
    FileTransferButton,
    FileTransferInput,
    FitToScreenButton,
    ScreenViewer,
    BlockInputButton,
    InviteButton,
    KeyboardButton,
    TouchKeyboardInput,
    MenuFrame,
    MenuButton,
    ScreenViewerWrapper,
    WindowsSessionSelect,
    FileTransferMenu,
    FileUploadButtton,
    FileDownloadButton,
    ViewOnlyButton,
    FullScreenButton,
    RequesterNameInput,
    SessionIDInput,
    ConnectForm,
    CloseAllPopupMenus,
    ExtrasMenu,
    ExtrasMenuButton,
    WindowsSessionMenuButton,
    WindowsSessionMenu,
    MetricsButton,
    MetricsFrame,
    SetStatusMessage,
    PanelButton,
    SidePanel,
    SidePanelClose,
    RestartButton,
    ClipboardSyncButton,
    SendClipboardButton,
    WaitOverlayCancel,
    Confirm,
    PromptText,
    ShortcutsButton,
    ShortcutsMenu,
    RestartSafeButton,
    SessionEndedClose,
    ShowWaitOverlay,
    HideWaitOverlay,
} from "./UI.js";
import { SetSettings } from "./SettingsService.js";
import { Sound } from "./Sound.js";
import { ViewerApp } from "./App.js";
import { Point } from "./Models/Point.js";
import { UploadFiles, CancelCurrentUpload } from "./FileTransferService.js";
import { PickFolderAndUpload, PickFilesAndDownload } from "./FolderPicker.js";
import { RemoteControlMode } from "./Enums/RemoteControlMode.js";
import { GetDistanceBetween } from "./Utilities.js";
import { ShowToast } from "./UI.js";

var isDragging: boolean;
var currentPointerDevice: string;
var currentTouchCount: number;
var isPinchZooming: boolean;
var startPinchPoint1: Point;
var startPinchPoint2: Point;
var lastPinchDistance: number;
var longPressStarted: boolean;
var longPressStartOffsetX: number;
var longPressStartOffsetY: number;
var lastPinchCenterX: number;
var lastPinchCenterY: number;
var isScrolling: boolean;
var lastScrollTime: number;
var lastScrollTouchY1: number;
var lastScrollTouchY2: number;
var lastPointerMove = Date.now();
var pendingPaste = false;
// Tecla Windows: só vai para o computador remoto junto com outra tecla (Win+E) ou sozinha ao soltar.
// Se o Windows daqui capturar o atalho (Win+R abre o Executar aqui), a janela perde o foco e a tecla
// é descartada, em vez de abrir o menu Iniciar lá.
var metaHeld = false;
// As teclas vão para o computador remoto na ordem em que foram apertadas (uma de cada vez).
var keyQueue: Promise<unknown> = Promise.resolve();
function QueueKey(send: () => Promise<unknown>): Promise<unknown> {
    keyQueue = keyQueue.then(send).catch(() => { });
    return keyQueue;
}
var metaSent = false;
var pendingPasteTimer = -1;
var suppressVKeyUp = false;
var pointerMoveTimeout = -1;

export function ApplyInputHandlers() {
    AudioButton.addEventListener("click", async (ev) => {
        AudioButton.classList.toggle("toggled");
        var toggleOn = AudioButton.classList.contains("toggled");
        if (toggleOn) {
            Sound.Init();
        }
        await ViewerApp.MessageSender.SendToggleAudio(toggleOn);
    });
    ChangeScreenButton.addEventListener("click", (ev) => {
        ev.stopPropagation();

        CloseAllPopupMenus(ScreenSelectMenu.id);

        // This could be put into a re-usable "openPopup" function that takes
        // "target element" and "placement" as inputs, but all this is
        // temporary, so I don't think it's worth the time.
        const x = ChangeScreenButton.getBoundingClientRect().left;
        const left = `${x.toFixed(0)}px`;
        const y = ChangeScreenButton.getBoundingClientRect().bottom;
        const top = `${y.toFixed(0)}px`;

        ScreenSelectMenu.style.left = left;
        ScreenSelectMenu.style.top = top;
        ScreenSelectMenu.classList.toggle("open");

        window.addEventListener(
            "click",
            () => {
                CloseAllPopupMenus(null);
            },
            { once: true }
        );
    });
    ClipboardTransferButton.addEventListener("click", (ev) => {
        ev.stopPropagation();

        CloseAllPopupMenus(ClipboardTransferMenu.id);

        const x = ClipboardTransferButton.getBoundingClientRect().left;
        const left = `${x.toFixed(0)}px`;
        const y = ClipboardTransferButton.getBoundingClientRect().bottom;
        const top = `${y.toFixed(0)}px`;

        ClipboardTransferMenu.style.left = left;
        ClipboardTransferMenu.style.top = top;
        ClipboardTransferMenu.classList.toggle("open");

        window.addEventListener(
            "click",
            () => {
                CloseAllPopupMenus(null);
            },
            { once: true }
        );
    });
    ViewOnlyButton.addEventListener("click", () => {
        ViewOnlyButton.classList.toggle("toggled");
        ViewerApp.ViewOnlyMode = ViewOnlyButton.classList.contains("toggled");
    });
    TypeClipboardButton.addEventListener("click", async (ev) => {
        CloseAllPopupMenus(null);
        if (ViewerApp.ViewOnlyMode) {
            ShowToast("O modo somente visualização está ativado.");
            return;
        }
        // Já vem com o texto copiado, quando o navegador deixa ler.
        let initial = "";
        try {
            if (window.isSecureContext && navigator.clipboard?.readText) {
                initial = await navigator.clipboard.readText();
            }
        }
        catch { }
        const text = await PromptText(
            "Digitar texto no computador remoto",
            "O texto é digitado tecla por tecla onde estiver o cursor lá. Use onde colar não funciona: tela de login do Windows, janela de administrador (UAC), chave do BitLocker, programas que bloqueiam Ctrl+V.",
            "Clique antes no campo de destino na tela remota. Ctrl+Enter digita.",
            "Digitar",
            initial);
        if (!text) {
            return;
        }
        await ViewerApp.MessageSender.SendTextTransfer(text, true);
        ShowToast("Texto digitado no computador remoto.");
    });
    ConnectButton.addEventListener("click", () => {
        if (!ConnectForm.checkValidity()) {
            return;
        }
        ViewerApp.ConnectToClient();
    });
    CtrlAltDelButton.addEventListener("click", async () => {
        if (ViewerApp.ViewOnlyMode) {
            alert("O modo somente visualização está ativado.");
            return;
        }

        CloseAllPopupMenus(null);
        await ViewerApp.MessageSender.SendCtrlAltDel();
    });
    DisconnectButton.addEventListener("click", (ev) => {
        ConnectButton.removeAttribute("disabled");
        ConnectButton.innerText = "Conectar";
        SetStatusMessage("Sessão encerrada por você.");
        ViewerApp.EndedByTechnician = true;
        ViewerApp.ViewerHubConnection.Connection.stop();
    });
    document.getElementById("sessionEndedReconnect")?.addEventListener("click", () => Reconnect());
    SessionEndedClose.addEventListener("click", () => {
        window.close();
        window.setTimeout(() => ShowToast("O navegador não deixou fechar esta aba. Feche pelo X da aba."), 300);
    });

    [SessionIDInput, RequesterNameInput].forEach((x) => {
        x.addEventListener("keypress", (ev: KeyboardEvent) => {
            if (!SessionIDInput.value || !RequesterNameInput.value) {
                return;
            }

            if (ev.key.toLowerCase() == "enter") {
                ViewerApp.ConnectToClient();
            }
        });

        x.addEventListener("input", () => {
            if (!SessionIDInput.value || !RequesterNameInput.value) {
                ConnectButton.setAttribute("disabled", "disabled");
            } else {
                ConnectButton.removeAttribute("disabled");
            }
        });
    });
    ExtrasMenuButton.addEventListener("click", (ev) => {
        ev.stopPropagation();

        CloseAllPopupMenus(ExtrasMenu.id);

        const x =
            document.body.clientWidth -
            ExtrasMenuButton.getBoundingClientRect().right;
        const right = `${x.toFixed(0)}px`;
        const y = ExtrasMenuButton.getBoundingClientRect().bottom;
        const top = `${y.toFixed(0)}px`;

        ExtrasMenu.style.right = right;
        ExtrasMenu.style.top = top;
        ExtrasMenu.classList.toggle("open");

        window.addEventListener(
            "click",
            () => {
                CloseAllPopupMenus(null);
            },
            { once: true }
        );
    });
    FileTransferButton.addEventListener("click", (ev) => {
        ev.stopPropagation();

        CloseAllPopupMenus(FileTransferMenu.id);

        const x =
            document.body.clientWidth -
            FileTransferButton.getBoundingClientRect().right;
        const right = `${x.toFixed(0)}px`;
        const y = FileTransferButton.getBoundingClientRect().bottom;
        const top = `${y.toFixed(0)}px`;

        FileTransferMenu.style.right = right;
        FileTransferMenu.style.top = top;
        FileTransferMenu.classList.toggle("open");
        FileTransferMenu.style.zIndex = "6";

        window.addEventListener(
            "click",
            () => {
                CloseAllPopupMenus(null);
            },
            { once: true }
        );
    });
    FileUploadButtton.addEventListener("click", (ev) => {
        CloseAllPopupMenus(null);
        PickFolderAndUpload();
    });
    FileDownloadButton.addEventListener("click", async (ev) => {
        CloseAllPopupMenus(null);
        // Sem nada na tela do usuário até o técnico escolher os arquivos.
        PickFilesAndDownload();
    });
    document.getElementById("fileTransferCancel")?.addEventListener("click", () => CancelCurrentUpload());

    // Ctrl+V na tela remota: cola lá o que foi copiado aqui (texto ou arquivos).
    document.addEventListener("paste", async (e: ClipboardEvent) => {
        if (!pendingPaste) {
            return;
        }
        e.preventDefault();
        pendingPaste = false;
        window.clearTimeout(pendingPasteTimer);

        const files = Array.from(e.clipboardData?.files || []);
        if (files.length > 0) {
            if (ViewerApp.Settings.ViewerOptions?.AllowFiles === false) {
                ShowToast("Seu perfil não permite enviar arquivos.");
                return;
            }
            ShowToast(files.length == 1 ? `Colando ${files[0].name} no computador remoto...` : `Colando ${files.length} arquivos no computador remoto...`);
            await UploadFiles(files, "paste");
            return;
        }

        const text = e.clipboardData?.getData("text/plain");
        if (text && ViewerApp.Settings.ViewerOptions?.AllowClipboard !== false) {
            ViewerApp.ClipboardWatcher.LastClipboardText = text;
            await ViewerApp.MessageSender.SendTextTransfer(text, false);
        }
        QueueKey(() => ViewerApp.MessageSender.SendKeyPress("v"));
    });
    FileTransferInput.addEventListener("change", (ev) => {
        const destination = ViewerApp.PendingUploadDestination;
        ViewerApp.PendingUploadDestination = null;
        UploadFiles(FileTransferInput.files, destination);
    });
    FitToScreenButton.addEventListener("click", (ev) => {
        FitToScreenButton.classList.toggle("toggled");
        if (FitToScreenButton.classList.contains("toggled")) {
            ScreenViewer.classList.add("fit");
        } else {
            ScreenViewer.classList.remove("fit");
        }
    });
    FullScreenButton.addEventListener("click", async () => {
        try {
            if (document.fullscreenElement) {
                await document.exitFullscreen();
            } else {
                await document.body.requestFullscreen();
            }
        }
        catch (e) {
            console.warn("Fullscreen request failed.", e);
            ShowToast("O navegador não permitiu a tela cheia.");
        }
    });
    // O botão acompanha a tela cheia mesmo quando ela é encerrada pelo Esc ou pelo navegador.
    // Em tela cheia, o Keyboard Lock (Chrome/Edge) manda Win, Alt+Tab e Esc ao computador remoto.
    document.addEventListener("fullscreenchange", async () => {
        const isFullscreen = !!document.fullscreenElement;
        FullScreenButton.classList.toggle("toggled", isFullscreen);
        const keyboard = (navigator as any).keyboard;
        if (!keyboard?.lock) {
            return;
        }
        try {
            if (isFullscreen && !ViewerApp.ViewOnlyMode) {
                await keyboard.lock();
                ShowToast("Atalhos do Windows vão para o computador remoto. Segure Esc para sair da tela cheia.");
            }
            else {
                keyboard.unlock();
            }
        }
        catch (e) {
            console.warn("Keyboard lock unavailable.", e);
        }
    });
    BlockInputButton.addEventListener("click", async (ev) => {
        if (ViewerApp.ViewOnlyMode) {
            alert("O modo somente visualização está ativado.");
            return;
        }
        BlockInputButton.classList.toggle("toggled");
        if (BlockInputButton.classList.contains("toggled")) {
            await ViewerApp.MessageSender.SendToggleBlockInput(true);
        } else {
            await ViewerApp.MessageSender.SendToggleBlockInput(false);
        }
    });
    InviteButton.addEventListener("click", (ev) => {
        var url = "";
        if (ViewerApp.Mode == RemoteControlMode.Attended) {
            url = `${location.origin}${location.pathname}?sessionId=${ViewerApp.SessionId}`;
        } else {
            url = `${location.origin}${location.pathname}?mode=Unattended&sessionId=${ViewerApp.SessionId}&accessKey=${ViewerApp.AccessKey}`;
        }
        CopyText(url).then(copied => {
            ShowToast(copied ? "Link copiado para a área de transferência." : "Não foi possível copiar o link.");
        });
    });
    KeyboardButton.addEventListener("click", (ev) => {
        CloseAllPopupMenus(null);
        KeyboardButton.classList.toggle("toggled");
        if (KeyboardButton.classList.contains("toggled")) {
            TouchKeyboardInput.focus();
        }
        else {
            TouchKeyboardInput.blur();
        }
    });
    MenuButton.addEventListener("click", (ev) => {
        const open = !MenuFrame.classList.contains("open");
        SetToolbarOpen(open);
        ViewerApp.Settings.ToolbarCollapsed = !open;
        SetSettings(ViewerApp.Settings);
        CloseAllPopupMenus(null);
    });
    SetToolbarOpen(ViewerApp.Settings.ToolbarCollapsed !== true);

    PanelButton.addEventListener("click", (ev) => {
        ev.stopPropagation();
        CloseAllPopupMenus(null);
        ToggleSidePanel(SidePanel.classList.contains("d-none"));
    });
    SidePanelClose.addEventListener("click", () => ToggleSidePanel(false));

    RestartButton.addEventListener("click", async (ev) => {
        ev.stopPropagation();
        CloseAllPopupMenus(null);
        if (ViewerApp.ViewOnlyMode) {
            ShowToast("O modo somente visualização está ativado.");
            return;
        }
        const confirmed = await Confirm(
            "Reiniciar o computador?",
            "O computador reinicia agora. Arquivos abertos e não salvos podem ser perdidos. " +
            "Esta janela espera ele voltar e reconecta sozinha.",
            "Reiniciar");
        if (!confirmed) {
            return;
        }
        await RestartRemote("normal");
    });

    RestartSafeButton.addEventListener("click", async (ev) => {
        ev.stopPropagation();
        CloseAllPopupMenus(null);
        if (ViewerApp.ViewOnlyMode) {
            ShowToast("O modo somente visualização está ativado.");
            return;
        }
        const confirmed = await Confirm(
            "Reiniciar em modo de segurança?",
            "O computador reinicia em modo de segurança com rede, só desta vez, e esta janela reconecta sozinha. " +
            "Para voltar ao Windows normal, use \"Reiniciar e voltar\" quando terminar a manutenção.",
            "Reiniciar em modo de segurança");
        if (!confirmed) {
            return;
        }
        await RestartRemote("safe");
    });

    WaitOverlayCancel.addEventListener("click", () => {
        HideWaitOverlay();
        ViewerApp.IsWaitingForRestart = false;
        DisconnectButton.click();
    });

    ClipboardSyncButton.classList.toggle("toggled", ViewerApp.Settings.ClipboardSync !== false);
    ClipboardSyncButton.addEventListener("click", () => {
        const enabled = ViewerApp.Settings.ClipboardSync === false;
        ViewerApp.Settings.ClipboardSync = enabled;
        SetSettings(ViewerApp.Settings);
        ClipboardSyncButton.classList.toggle("toggled", enabled);
        if (enabled) {
            ViewerApp.ClipboardWatcher.WatchClipboard();
            ShowToast("Sincronização da área de transferência ligada.");
        }
        else {
            ViewerApp.ClipboardWatcher.Stop();
            ShowToast("Sincronização da área de transferência desligada.");
        }
    });

    SendClipboardButton.addEventListener("click", async (ev) => {
        CloseAllPopupMenus(null);
        if (ViewerApp.ViewOnlyMode) {
            ShowToast("O modo somente visualização está ativado.");
            return;
        }
        const text = await ReadClipboardText();
        if (text == null) {
            return;
        }
        await ViewerApp.MessageSender.SendTextTransfer(text, false);
        ShowToast("Texto colocado na área de transferência do computador remoto. Use Ctrl+V lá.");
    });

    ShortcutsButton.addEventListener("click", (ev) => {
        ev.stopPropagation();
        CloseAllPopupMenus(ShortcutsMenu.id);
        const rect = ShortcutsButton.getBoundingClientRect();
        ShortcutsMenu.style.left = `${rect.left.toFixed(0)}px`;
        ShortcutsMenu.style.top = `${rect.bottom.toFixed(0)}px`;
        ShortcutsMenu.classList.toggle("open");
        window.addEventListener("click", () => CloseAllPopupMenus(null), { once: true });
    });
    ShortcutsMenu.querySelectorAll<HTMLButtonElement>("button[data-keys]").forEach(button => {
        button.addEventListener("click", async () => {
            CloseAllPopupMenus(null);
            if (ViewerApp.ViewOnlyMode) {
                ShowToast("O modo somente visualização está ativado.");
                return;
            }
            await SendShortcut(button.dataset.keys);
        });
    });

    MetricsButton.addEventListener("click", () => {
        MetricsFrame.classList.toggle("d-none");
    });

    ScreenViewer.addEventListener("pointerup", async ev => {
        if (ViewerApp.ViewOnlyMode) {
            return;
        }

        const percentX = ev.offsetX / ScreenViewer.clientWidth;
        const percentY = ev.offsetY / ScreenViewer.clientHeight;

        if (longPressStarted && !isDragging) {
            await ViewerApp.MessageSender.SendMouseDown(2, percentX, percentY);
            await ViewerApp.MessageSender.SendMouseUp(2, percentX, percentY);
        }

        if (longPressStarted && isDragging) {
            // If we're dragging, we need to send a mouse up event.
            await ViewerApp.MessageSender.SendMouseUp(0, percentX, percentY);
        }

        resetTouchState();
    });

    ScreenViewer.addEventListener("pointercancel", ev => {
        resetTouchState();
    });

    ScreenViewer.addEventListener("pointerout", ev => {
        resetTouchState();
    });

    ScreenViewer.addEventListener("pointerleave", ev => {
        resetTouchState();
    });

    ScreenViewer.addEventListener("touchmove", async function (e: TouchEvent) {
        currentTouchCount = e.touches.length;

        if (longPressStarted || isDragging) {
            e.preventDefault();
        }

        if (e.touches.length == 1 && longPressStarted && !isDragging) {
            e.stopPropagation();

            if (ViewerApp.ViewOnlyMode) {
                return;
            }

            const rect = ScreenViewer.getBoundingClientRect();
            const offsetX = e.touches[0].pageX - rect.left;
            const offsetY = e.touches[0].pageY - rect.top;

            const moveDistance = GetDistanceBetween(
                longPressStartOffsetX,
                longPressStartOffsetY,
                offsetX,
                offsetY
            );

            if (moveDistance > 5) {
                isDragging = true;
                // Move the mouse to the initial touch point to start the drag operation.
                const percentX = longPressStartOffsetX / ScreenViewer.clientWidth;
                const percentY = longPressStartOffsetY / ScreenViewer.clientHeight;
                await ViewerApp.MessageSender.SendMouseMove(percentX, percentY);
                await ViewerApp.MessageSender.SendMouseDown(0, percentX, percentY);
            }
            return;
        }

        if (isDragging) {
            e.stopPropagation();

            if (ViewerApp.ViewOnlyMode) {
                return;
            }

            var screenViewerLeft = ScreenViewer.getBoundingClientRect().left;
            var screenViewerTop = ScreenViewer.getBoundingClientRect().top;
            var pagePercentX =
                (e.touches[0].pageX - screenViewerLeft) / ScreenViewer.clientWidth;
            var pagePercentY =
                (e.touches[0].pageY - screenViewerTop) / ScreenViewer.clientHeight;
            ViewerApp.MessageSender.SendMouseMove(pagePercentX, pagePercentY);

            return;
        }

        if (e.touches.length == 2) {
            let touchMove1 = lastScrollTouchY1 - e.touches[0].pageY;
            let touchMove2 = lastScrollTouchY2 - e.touches[1].pageY;

            if (!isPinchZooming && (isScrolling || touchMove1 * touchMove2 > 0)) {
                // Both touch points are moving in the same direction.  We're doing a scroll.

                if (!isScrolling) {
                    // If this is the start of scrolling, move the mouse to our touch point so
                    // the scroll wheel action will target the intended element on screen.
                    var screenViewerLeft = ScreenViewer.getBoundingClientRect().left;
                    var screenViewerTop = ScreenViewer.getBoundingClientRect().top;
                    var pagePercentX =
                        (e.touches[0].pageX - screenViewerLeft) / ScreenViewer.clientWidth;
                    var pagePercentY =
                        (e.touches[0].pageY - screenViewerTop) / ScreenViewer.clientHeight;
                    await ViewerApp.MessageSender.SendMouseMove(
                        pagePercentX,
                        pagePercentY
                    );
                }

                isScrolling = true;
                if (Date.now() - lastScrollTime < 100) {
                    return;
                }
                lastScrollTime = Date.now();
                let yMove = Math.max(-1, Math.min(touchMove1, 1));
                await ViewerApp.MessageSender.SendMouseWheel(0, yMove);
                lastScrollTouchY1 = e.touches[0].pageY;
                return;
            }

            var pinchPoint1 = {
                X: e.touches[0].pageX,
                Y: e.touches[0].pageY,
                IsEmpty: false,
            };
            var pinchPoint2 = {
                X: e.touches[1].pageX,
                Y: e.touches[1].pageY,
                IsEmpty: false,
            };
            var pinchDistance = GetDistanceBetween(
                pinchPoint1.X,
                pinchPoint1.Y,
                pinchPoint2.X,
                pinchPoint2.Y
            );

            var pinchCenterX = (pinchPoint1.X + pinchPoint2.X) / 2;
            var pinchCenterY = (pinchPoint1.Y + pinchPoint2.Y) / 2;

            ScreenViewerWrapper.scrollBy(
                lastPinchCenterX - pinchCenterX,
                lastPinchCenterY - pinchCenterY
            );

            lastPinchCenterX = pinchCenterX;
            lastPinchCenterY = pinchCenterY;

            if (Math.abs(pinchDistance - lastPinchDistance) > 5) {
                isPinchZooming = true;
                if (FitToScreenButton.classList.contains("toggled")) {
                    FitToScreenButton.click();
                }

                var currentWidth = ScreenViewer.clientWidth;
                var currentHeight = ScreenViewer.clientHeight;

                var clientAdjustedScrollLeftPercent =
                    (ScreenViewerWrapper.scrollLeft +
                        ScreenViewerWrapper.clientWidth * 0.5) /
                    ScreenViewerWrapper.scrollWidth;
                var clientAdjustedScrollTopPercent =
                    (ScreenViewerWrapper.scrollTop +
                        ScreenViewerWrapper.clientHeight * 0.5) /
                    ScreenViewerWrapper.scrollHeight;

                var currentWidthPercent = Number(ScreenViewer.style.width.slice(0, -1));
                var newWidthPercent = Math.max(
                    100,
                    currentWidthPercent +
                    (pinchDistance - lastPinchDistance) * (currentWidthPercent / 100)
                );
                newWidthPercent = Math.min(5000, newWidthPercent);
                ScreenViewer.style.width = String(newWidthPercent) + "%";

                var heightChange = ScreenViewer.clientHeight - currentHeight;
                var widthChange = ScreenViewer.clientWidth - currentWidth;

                var pinchAdjustX = pinchCenterX / window.innerWidth - 0.5;
                var pinchAdjustY = pinchCenterY / window.innerHeight - 0.5;

                var scrollByX =
                    widthChange *
                    (clientAdjustedScrollLeftPercent +
                        (pinchAdjustX * ScreenViewerWrapper.clientWidth) /
                        ScreenViewerWrapper.scrollWidth);
                var scrollByY =
                    heightChange *
                    (clientAdjustedScrollTopPercent +
                        (pinchAdjustY * ScreenViewerWrapper.clientHeight) /
                        ScreenViewerWrapper.scrollHeight);

                ScreenViewerWrapper.scrollBy(scrollByX, scrollByY);

                lastPinchDistance = pinchDistance;
            }
            return;
        }
    });

    ScreenViewer.addEventListener("pointerdown", function (e: PointerEvent) {
        currentPointerDevice = e.pointerType;
    });

    ScreenViewer.addEventListener("pointerenter", function (e: PointerEvent) {
        currentPointerDevice = e.pointerType;
    });

    ScreenViewer.addEventListener("pointermove", function (e: PointerEvent) {
        currentPointerDevice = e.pointerType;
    });


    ScreenViewer.addEventListener("mousemove", async function (e: MouseEvent) {
        if (ViewerApp.ViewOnlyMode) {
            return;
        }

        if (pointerMoveTimeout > -1) {
            clearTimeout(pointerMoveTimeout);
        }

        var percentX = e.offsetX / ScreenViewer.clientWidth;
        var percentY = e.offsetY / ScreenViewer.clientHeight;
        const now = Date.now();

        // Throttle mouse movements so we're not sending too many messages rapidly.
        // The timeout ensures that the last movement still gets sent.
        if (now - lastPointerMove < 50) {
            pointerMoveTimeout = setTimeout(async () => {
                await ViewerApp.MessageSender.SendMouseMove(percentX, percentY);
            }, 60);
            return;
        }

        lastPointerMove = now;
        await ViewerApp.MessageSender.SendMouseMove(percentX, percentY);
    });

    ScreenViewer.addEventListener("mousedown", async function (e: MouseEvent) {
        if (ViewerApp.ViewOnlyMode) {
            return;
        }

        var percentX = e.offsetX / ScreenViewer.clientWidth;
        var percentY = e.offsetY / ScreenViewer.clientHeight;
        await ViewerApp.MessageSender.SendMouseDown(e.button, percentX, percentY);
    });

    ScreenViewer.addEventListener("mouseup", async function (e: MouseEvent) {
        if (ViewerApp.ViewOnlyMode) {
            return;
        }

        var percentX = e.offsetX / ScreenViewer.clientWidth;
        var percentY = e.offsetY / ScreenViewer.clientHeight;
        await ViewerApp.MessageSender.SendMouseUp(e.button, percentX, percentY);
    });

    ScreenViewer.addEventListener("touchstart", function (e: TouchEvent) {
        currentTouchCount = e.touches.length;

        if (currentTouchCount == 2) {
            lastScrollTouchY1 = e.touches[0].pageY;
            lastScrollTouchY2 = e.touches[1].pageY;
            startPinchPoint1 = {
                X: e.touches[0].pageX,
                Y: e.touches[0].pageY,
                IsEmpty: false,
            };
            startPinchPoint2 = {
                X: e.touches[1].pageX,
                Y: e.touches[1].pageY,
                IsEmpty: false,
            };
            lastPinchDistance = GetDistanceBetween(
                startPinchPoint1.X,
                startPinchPoint1.Y,
                startPinchPoint2.X,
                startPinchPoint2.Y
            );
            lastPinchCenterX = (startPinchPoint1.X + startPinchPoint2.X) / 2;
            lastPinchCenterY = (startPinchPoint1.Y + startPinchPoint2.Y) / 2;
        }
        isDragging = false;
        var focusedInput = document.querySelector(
            "input:focus"
        ) as HTMLInputElement;

        if (focusedInput) {
            focusedInput.blur();
        }
    });


    ScreenViewer.addEventListener("touchend", async function (e: TouchEvent) {
        currentTouchCount = e.touches.length;

        if (currentTouchCount == 0) {
            isPinchZooming = false;
            isScrolling = false;
            lastScrollTouchY1 = null;
            lastScrollTouchY2 = null;
            startPinchPoint1 = null;
            startPinchPoint2 = null;
        }

        var percentX =
            (e.changedTouches[0].pageX - ScreenViewer.getBoundingClientRect().left) /
            ScreenViewer.clientWidth;
        var percentY =
            (e.changedTouches[0].pageY - ScreenViewer.getBoundingClientRect().top) /
            ScreenViewer.clientHeight;

        if (longPressStarted && !isDragging && !ViewerApp.ViewOnlyMode) {
            await ViewerApp.MessageSender.SendMouseDown(2, percentX, percentY);
            await ViewerApp.MessageSender.SendMouseUp(2, percentX, percentY);
        }

        if (isDragging && !ViewerApp.ViewOnlyMode) {
            await ViewerApp.MessageSender.SendMouseUp(0, percentX, percentY);
        }

        longPressStarted = false;
        isDragging = false;
    });

    ScreenViewer.addEventListener("contextmenu", async (ev) => {
        ev.preventDefault();

        if (ViewerApp.ViewOnlyMode) {
            return;
        }

        if (currentPointerDevice == "touch") {
            // We're either starting a right-click or left-button drag.
            // Either way, we'll move the cursor to the initial touch point.
            const percentX =
                (ev.pageX - ScreenViewer.getBoundingClientRect().left) /
                ScreenViewer.clientWidth;
            const percentY =
                (ev.pageY - ScreenViewer.getBoundingClientRect().top) /
                ScreenViewer.clientHeight;
            await ViewerApp.MessageSender.SendMouseMove(percentX, percentY);

            longPressStarted = true;
            longPressStartOffsetX = ev.offsetX;
            longPressStartOffsetY = ev.offsetY;
        }
    });

    ScreenViewer.addEventListener("wheel", async function (e: WheelEvent) {
        e.preventDefault();
        if (ViewerApp.ViewOnlyMode) {
            return;
        }
        await ViewerApp.MessageSender.SendMouseWheel(e.deltaX, e.deltaY);
    });

    TouchKeyboardInput.addEventListener("keydown", async (ev) => {
        if (ev.key === "Enter" || ev.key === "Backspace") {
            await ViewerApp.MessageSender.SendKeyPress(ev.key);
        }
    });

    TouchKeyboardInput.addEventListener("input", async (ev) => {
        if (ViewerApp.ViewOnlyMode) {
            return;
        }

        const text = TouchKeyboardInput.value;
        TouchKeyboardInput.value = "";

        await ViewerApp.MessageSender.SendTextTransfer(text, true);
    });

    TouchKeyboardInput.addEventListener("blur", () => {
        if (KeyboardButton.classList.contains("toggled")) {
            TouchKeyboardInput.focus();
        }
    });

    WindowsSessionMenuButton.addEventListener("click", (ev) => {
        ev.stopPropagation();

        CloseAllPopupMenus(WindowsSessionMenu.id);

        const x =
            document.body.clientWidth -
            WindowsSessionMenuButton.getBoundingClientRect().right;
        const right = `${x.toFixed(0)}px`;
        const y = WindowsSessionMenuButton.getBoundingClientRect().bottom;
        const top = `${y.toFixed(0)}px`;

        WindowsSessionMenu.style.right = right;
        WindowsSessionMenu.style.top = top;
        WindowsSessionMenu.classList.toggle("open");

        window.addEventListener(
            "click",
            () => {
                CloseAllPopupMenus(null);
            },
            { once: true }
        );
    });
    WindowsSessionSelect.addEventListener("click", (ev) => {
        ev.stopPropagation();
    });
    WindowsSessionSelect.addEventListener("focus", async () => {
        await ViewerApp.MessageSender.GetWindowsSessions();
    });
    WindowsSessionSelect.addEventListener("change", async () => {
        SetStatusMessage("Trocando de sessão");
        ShowToast("Trocando de sessão");
        await ViewerApp.MessageSender.ChangeWindowsSession(
            Number(WindowsSessionSelect.selectedOptions[0].value)
        );
    });

    window.addEventListener("keydown", async function (e) {
        if (
            document.querySelector("input:focus") ||
            document.querySelector("textarea:focus")
        ) {
            return;
        }
        if (ViewerApp.ViewOnlyMode) {
            return;
        }
        // Ctrl+V: deixa o navegador entregar o evento "paste" (com o texto ou os arquivos
        // copiados aqui) e só depois manda o Ctrl+V para o computador remoto.
        if ((e.ctrlKey || e.metaKey) && !e.altKey && e.key.toLowerCase() == "v" &&
            ViewerApp.Settings.ClipboardSync !== false) {
            if (!e.repeat) {
                pendingPaste = true;
                suppressVKeyUp = true;
                window.clearTimeout(pendingPasteTimer);
                pendingPasteTimer = window.setTimeout(async () => {
                    if (pendingPaste) {
                        pendingPaste = false;
                        QueueKey(() => ViewerApp.MessageSender.SendKeyPress("v"));
                    }
                }, 400);
            }
            return;
        }
        if (!e.ctrlKey || !e.shiftKey || e.key.toLowerCase() != "i") {
            e.preventDefault();
        }
        if (e.key == "Meta" || e.key == "OS") {
            if (!e.repeat) {
                metaHeld = true;
                metaSent = false;
            }
            return;
        }
        if (metaHeld && !metaSent) {
            metaSent = true;
            QueueKey(() => ViewerApp.MessageSender.SendKeyDown("Meta"));
        }
        QueueKey(() => ViewerApp.MessageSender.SendKeyDown(e.key));
    });
    window.addEventListener("keyup", async function (e) {
        if (
            document.querySelector("input:focus") ||
            document.querySelector("textarea:focus")
        ) {
            return;
        }
        e.preventDefault();
        if (ViewerApp.ViewOnlyMode) {
            return;
        }
        if (e.key == "Meta" || e.key == "OS") {
            const wasSent = metaSent;
            const wasHeld = metaHeld;
            metaHeld = false;
            metaSent = false;
            if (wasSent) {
                QueueKey(() => ViewerApp.MessageSender.SendKeyUp("Meta"));
            }
            else if (wasHeld) {
                QueueKey(() => ViewerApp.MessageSender.SendKeyPress("Meta"));
            }
            return;
        }
        if (suppressVKeyUp && e.key.toLowerCase() == "v") {
            // O "v" do Ctrl+V já foi mandado (tecla inteira) junto com o que foi colado.
            suppressVKeyUp = false;
            return;
        }
        QueueKey(() => ViewerApp.MessageSender.SendKeyUp(e.key));
    });

    window.addEventListener("blur", async () => {
        if (ViewerApp.ViewOnlyMode) {
            return;
        }
        if (metaHeld && !metaSent) {
            // O Windows daqui pegou o atalho (ex.: Win+R). Nada vai para o computador remoto.
            ShowToast("Esse atalho abriu no seu computador. Use o menu Atalhos ou a tela cheia para mandar ao remoto.", 8000);
        }
        metaHeld = false;
        metaSent = false;
        QueueKey(() => ViewerApp.MessageSender.SendSetKeyStatesUp());
    });

    window.addEventListener("touchstart", () => {
        KeyboardButton.classList.remove("d-none");
    });

    window.ondragover = function (e) {
        e.preventDefault();
        e.dataTransfer.dropEffect = "copy";
    };
    window.ondrop = function (e) {
        e.preventDefault();
        if (e.dataTransfer.files.length < 1) {
            return;
        }
        // Solto sobre a tela remota: salva na pasta (ou Área de Trabalho) que está sob o ponto.
        const rect = ScreenViewer.getBoundingClientRect();
        let destination: string = null;
        if (rect.width > 0 && rect.height > 0 &&
            e.clientX >= rect.left && e.clientX <= rect.right &&
            e.clientY >= rect.top && e.clientY <= rect.bottom) {
            const x = (e.clientX - rect.left) / rect.width;
            const y = (e.clientY - rect.top) / rect.height;
            destination = `drop:${x.toFixed(4)};${y.toFixed(4)}`;
        }
        UploadFiles(e.dataTransfer.files, destination);
    };
}

function resetTouchState() {
    longPressStarted = false;
    isDragging = false;
    isPinchZooming = false;
    isScrolling = false;
}

/** Lê o texto copiado no navegador do técnico. Avisa quando o navegador não deixa. */
async function ReadClipboardText(): Promise<string | null> {
    if (!window.isSecureContext || !navigator.clipboard?.readText) {
        ShowToast("O navegador só libera a área de transferência em HTTPS.");
        return null;
    }
    try {
        const text = await navigator.clipboard.readText();
        if (!text) {
            ShowToast("A área de transferência está vazia.");
            return null;
        }
        return text;
    }
    catch (e) {
        console.log("Unable to read clipboard.", e);
        ShowToast("Não foi possível ler a área de transferência. Libere a permissão no navegador.");
        return null;
    }
}

async function CopyText(text: string): Promise<boolean> {
    try {
        await navigator.clipboard.writeText(text);
        return true;
    }
    catch {
        return false;
    }
}

function SetToolbarOpen(open: boolean) {
    MenuFrame.classList.toggle("open", open);
    MenuButton.classList.toggle("open", open);
    MenuButton.title = open ? "Recolher a barra" : "Mostrar a barra";
}

/** Painel lateral com dados, terminal, scripts e envio de arquivo do computador (página do servidor). */
export function ToggleSidePanel(show: boolean) {
    const frame = document.getElementById("sidePanelFrame") as HTMLIFrameElement;
    if (show && !ViewerApp.DeviceId) {
        return;
    }
    if (show && !frame.getAttribute("src")) {
        frame.setAttribute("src", `/viewer-panel/${encodeURIComponent(ViewerApp.DeviceId)}`);
    }
    SidePanel.classList.toggle("d-none", !show);
    PanelButton.classList.toggle("toggled", show);
    if (!show) {
        ScreenViewer.focus?.();
    }
}

async function RestartRemote(mode: "normal" | "safe") {
    const result = await ViewerApp.ViewerHubConnection.RestartDevice(mode);
    if (!result?.IsSuccess) {
        ShowToast(result?.Reason || "Não foi possível reiniciar o computador.");
        return;
    }
    ViewerApp.IsWaitingForRestart = true;
    ShowWaitOverlay(
        mode == "safe" ? "Reiniciando em modo de segurança" : "Reiniciando o computador",
        "O acesso volta sozinho quando o computador ligar de novo. Pode levar alguns minutos.");
}

/** "Reconectar": abre uma sessão nova no mesmo computador e troca esta página por ela. */
async function Reconnect() {
    const button = document.getElementById("sessionEndedReconnect") as HTMLButtonElement;
    const hint = document.getElementById("sessionEndedHint");
    if (!ViewerApp.DeviceId || !button) {
        return;
    }
    button.disabled = true;
    button.innerHTML = '<i class="fas fa-circle-notch fa-spin"></i> Reconectando...';
    hint.textContent = "Abrindo o acesso no computador. Se o aceite estiver ligado, o usuário precisa permitir de novo.";
    try {
        const response = await fetch("/api/viewer/reconnect", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ deviceId: ViewerApp.DeviceId, viewOnly: ViewerApp.ViewOnlyMode })
        });
        const contentType = response.headers.get("content-type") || "";
        const data = contentType.includes("json") ? await response.json() : null;
        if (response.ok && data?.url) {
            location.replace(data.url);
            return;
        }
        hint.textContent = data?.error || (response.status == 401 || response.redirected
            ? "Sua sessão no painel expirou. Entre de novo e abra o controle remoto pelo painel."
            : "Não foi possível reconectar. Tente pelo painel.");
    }
    catch {
        hint.textContent = "Sem conexão com o servidor. Tente de novo.";
    }
    button.disabled = false;
    button.innerHTML = '<i class="fas fa-rotate"></i> Reconectar';
}

/** "Meta+r" → Win pressionado, R, solta R, solta Win (com pequenas pausas, como uma pessoa). */
async function SendShortcut(keys: string) {
    const parts = (keys || "").split("+").filter(x => x);
    if (parts.length == 0) {
        return;
    }
    if (parts.length == 1) {
        await ViewerApp.MessageSender.SendKeyPress(parts[0]);
        return;
    }
    for (const key of parts) {
        await ViewerApp.MessageSender.SendKeyDown(key);
        await new Promise(resolve => window.setTimeout(resolve, 30));
    }
    for (const key of parts.slice().reverse()) {
        await ViewerApp.MessageSender.SendKeyUp(key);
        await new Promise(resolve => window.setTimeout(resolve, 20));
    }
}
