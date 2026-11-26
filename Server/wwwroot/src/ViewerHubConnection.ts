import * as UI from "./UI.js";
import { ViewerApp } from "./App.js";
import { CursorInfo } from "./Models/CursorInfo.js";
import { RemoteControlMode } from "./Enums/RemoteControlMode.js";
import { ShowToast } from "./UI.js";
import { WindowsSession } from "./Models/WindowsSession.js";
import { DtoType } from "./Enums/DtoType.js";
import { HubConnection } from "./Models/HubConnection.js";
import { ChunkDto } from "./DtoChunker.js";
import { MessagePack } from "./Interfaces/MessagePack.js";
import { ProcessStream } from "./CaptureProcessor.js";
import { HubConnectionState } from "./Enums/HubConnectionState.js";
import { StreamingState } from "./Models/StreamingState.js";
import { Result } from "./Models/Result.js";
import { RemoteControlViewerOptions } from "./Interfaces/Dtos.js";
import { GetSettings, SetSettings } from "./SettingsService.js";

const MsgPack: MessagePack = window["MessagePack"];

var signalR = window["signalR"];

export class ViewerHubConnection {
    Connection: HubConnection;
    PartialCaptureFrames: Uint8Array[] = [];
    CloseToken: string = "";
    private _closingSent = false;


    Connect() {
        if (this.Connection) {
            this.Connection.stop();
        }

        this.Connection = new signalR.HubConnectionBuilder()
            .withUrl("/hubs/viewer")
            .withHubProtocol(new signalR.protocols.msgpack.MessagePackHubProtocol())
            .withKeepAliveInterval(5000)
            .configureLogging(signalR.LogLevel.Information)
            .build();

        this.CloseToken = "";
        this._closingSent = false;

        this.ApplyMessageHandlers(this.Connection);

        this.Connection.start().then(async () => {
            try {
                this.CloseToken = await this.Connection.invoke<string>("GetCloseToken");
            }
            catch (e) {
                console.warn("Could not get close token.", e);
            }
            this.SendScreenCastRequestToDevice();
        }).catch(err => {
            console.error(err.toString());
            console.log("Conexão encerrada.");
            UI.StatusMessage.textContent = `Erro de conexão: ${err.message}`;
            UI.ToggleConnectUI(true);
        });

        this.Connection.onclose(() => {
            ViewerApp.IsWaitingForRestart = false;
            UI.HideWaitOverlay();
            ViewerApp.ClipboardWatcher.Stop();
            if (!UI.StatusMessage.innerText) {
                UI.SetStatusMessage("Conexão encerrada.");
            }
            UI.ToggleConnectUI(true);
        });

    }

    /**
     * Chamado quando a aba ou o navegador fecha sem clicar no X: avisa o servidor
     * pelo hub e por sendBeacon, para a sessão terminar na hora no computador do usuário.
     */
    NotifyClosing() {
        if (this._closingSent) {
            return;
        }
        this._closingSent = true;

        try {
            if (this.Connection?.state == HubConnectionState.Connected) {
                this.Connection.send("ViewerClosing");
            }
        }
        catch { }

        try {
            if (this.CloseToken && navigator.sendBeacon) {
                navigator.sendBeacon("/api/viewer/closed", new Blob([this.CloseToken], { type: "text/plain" }));
            }
        }
        catch { }
    }

    /** Esconde o que o perfil do técnico não permite. O servidor também bloqueia. */
    ApplyPermissions(options: RemoteControlViewerOptions) {
        const hide = (element: HTMLElement, condition: boolean) => {
            if (element && condition) {
                element.classList.add("d-none");
                element.setAttribute("disabled", "disabled");
            }
        };

        const noInput = options.AllowInput === false;
        const noSystem = options.AllowSystemActions === false || noInput;

        ViewerApp.DeviceId = options.DeviceId || "";
        if (UI.PanelButton) {
            UI.PanelButton.classList.toggle("d-none", !ViewerApp.DeviceId);
        }
        if (UI.SidePanelPopout && ViewerApp.DeviceId) {
            UI.SidePanelPopout.href = `/device-details/${encodeURIComponent(ViewerApp.DeviceId)}`;
        }

        // Copiar e colar automático só começa com a sessão aceita e se o perfil permitir.
        if (options.AllowClipboard !== false && !noInput) {
            ViewerApp.ClipboardWatcher.WatchClipboard();
        }
        hide(UI.ClipboardSyncButton, options.AllowClipboard === false || noInput);
        hide(UI.RestartButton, noSystem || ViewerApp.Mode != RemoteControlMode.Unattended);

        if (noInput) {
            ViewerApp.ViewOnlyMode = true;
            UI.ViewOnlyButton.classList.add("toggled");
        }
        hide(UI.ViewOnlyButton, noInput);
        hide(UI.KeyboardButton, noInput);
        hide(UI.FileTransferButton, options.AllowFiles === false);
        hide(UI.ClipboardTransferButton, options.AllowClipboard === false || noInput);
        hide(UI.BlockInputButton, noSystem);
        hide(UI.CtrlAltDelButton, noSystem);
        hide(UI.WindowsSessionMenuButton, noSystem);

        if (noInput) {
            ShowToast("Sessão só de visualização (perfil de acesso).");
        }
        else if (options.AllowFiles === false || options.AllowClipboard === false || options.AllowSystemActions === false) {
            ShowToast("Seu perfil de acesso limita algumas ações nesta sessão.");
        }
    }

    /** Reinicia o computador; a sessão continua e reconecta quando ele voltar. */
    async RestartDevice(): Promise<Result> {
        if (this.Connection?.state != HubConnectionState.Connected) {
            return { IsSuccess: false, Reason: "Sem conexão com o servidor." } as Result;
        }
        try {
            return await this.Connection.invoke<Result>("RestartDevice");
        }
        catch (e) {
            console.error(e);
            return { IsSuccess: false, Reason: "Não foi possível reiniciar o computador." } as Result;
        }
    }

    async ChangeWindowsSession(sessionID: number) {
        if (ViewerApp.Mode == RemoteControlMode.Unattended) {
            await this.Connection.invoke("ChangeWindowsSession", sessionID);
        }
    }

    async GetRemoteControlViewerOptions(): Promise<RemoteControlViewerOptions> {
        const settings = GetSettings();

        try {
            settings.ViewerOptions = await this.Connection.invoke<RemoteControlViewerOptions>("GetViewerOptions");
            SetSettings(settings);
            return settings.ViewerOptions;
        }
        catch (e) {
            console.error("Error while getting viewer options from server.", e);
            return settings.ViewerOptions;
        }
    }

    async InvokeCtrlAltDel() {
        if (this.Connection?.state != HubConnectionState.Connected) {
            return;
        }

        await this.Connection.invoke("InvokeCtrlAltDel");
    }

    async SendDtoToClient<T>(dto: T, type: DtoType): Promise<void> {

        if (this.Connection?.state != HubConnectionState.Connected) {
            return;
        }

        let chunks = ChunkDto(dto, type);

        for (var i = 0; i < chunks.length; i++) {
            const chunk = MsgPack.encode(chunks[i]);
            await this.Connection.invoke("SendDtoToClient", chunk);
        }
    }

    // Subject is an interface that comes from the SignalR library.
    // But we can't use the TypeScript library like we would in
    // React/Vue, so we have to use "any" here.  This won't be an
    // issue when we rewrite the front-end.
    async SendRecordingChunks(subject: any) {
        await this.Connection.send("StoreSessionRecording", subject);
    }

    async SendScreenCastRequestToDevice() {
        const viewerOptions = await this.GetRemoteControlViewerOptions();

        const result = await this.Connection.invoke<Result>(
            "SendScreenCastRequestToDevice",
            ViewerApp.SessionId,
            ViewerApp.AccessKey,
            ViewerApp.RequesterName);

        if (!result.IsSuccess) {
            ViewerApp.IsWaitingForRestart = false;
            UI.HideWaitOverlay();
            this.Connection.stop();
            UI.SetStatusMessage(result.Reason);
            return;
        }

        // As permissões do perfil só valem depois que a sessão é aceita.
        this.ApplyPermissions(await this.GetRemoteControlViewerOptions());

        const streamingState = new StreamingState();
        ProcessStream(streamingState);

        if (viewerOptions.ShouldRecordSession) {
            ViewerApp.SessionRecorder.Start();
        }

        this.Connection.stream("GetDesktopStream")
            .subscribe({
                next: (chunk: Uint8Array) => {
                    streamingState.ReceivedChunks.push(chunk);
                },
                complete: () => {
                    streamingState.StreamEnded = true;
                    ViewerApp.SessionRecorder.Stop();
                    if (ViewerApp.IsWaitingForRestart) {
                        return;
                    }
                    if (!UI.StatusMessage.innerText) {
                        UI.SetStatusMessage("Transmissão encerrada.");
                    }
                    UI.ToggleConnectUI(true);
                },
                error: (err) => {
                    console.warn(err);
                    streamingState.StreamEnded = true;
                    ViewerApp.SessionRecorder.Stop();
                    if (ViewerApp.IsWaitingForRestart) {
                        return;
                    }
                    if (!UI.StatusMessage.innerText) {
                        UI.SetStatusMessage("Transmissão encerrada.");
                    }
                    UI.ToggleConnectUI(true);
                },
            });

    }


    private ApplyMessageHandlers(hubConnection) {
        hubConnection.on("SendDtoToViewer", async (dto: ArrayBuffer) => {
            await ViewerApp.DtoMessageHandler.ParseBinaryMessage(dto);
        });

        hubConnection.on("ConnectionFailed", () => {
            UI.ConnectButton.removeAttribute("disabled");
            UI.ConnectButton.innerText = "Conectar";
            UI.SetStatusMessage("A conexão falhou ou foi negada.");
            ShowToast("A conexão falhou. Conecte novamente.");
            this.Connection.stop();
        });
        hubConnection.on("ReconnectFailed", () => {
            UI.ConnectButton.removeAttribute("disabled");
            UI.ConnectButton.innerText = "Conectar";
            UI.SetStatusMessage("Não foi possível reconectar.");
            ShowToast("Não foi possível reconectar.");
            this.Connection.stop();
        });
        hubConnection.on("ConnectionRequestDenied", () => {
            UI.ConnectButton.innerText = "Conectar";
            this.Connection.stop();
            UI.SetStatusMessage("Pedido de conexão negado.");
            ShowToast("Pedido de conexão negado.");
        });
        hubConnection.on("ViewerRemoved", () => {
            UI.ConnectButton.removeAttribute("disabled");
            UI.ConnectButton.innerText = "Conectar";
            UI.SetStatusMessage("A sessão foi encerrada pelo outro lado.");
            ShowToast("Sessão encerrada");
            this.Connection.stop();
        });
        hubConnection.on("ScreenCasterDisconnected", () => {
            UI.SetStatusMessage("O computador remoto desconectou.");
            this.Connection.stop();
        });
        hubConnection.on("RelaunchedScreenCasterReady", (newSessionId: string, newAccessKey: string) => {
            const newUrl =
                `${location.origin}${location.pathname}` +
                `?mode=Unattended&sessionId=${newSessionId}&accessKey=${newAccessKey}&viewOnly=${ViewerApp.ViewOnlyMode}`;
            window.history.replaceState(null, "", newUrl);
            if (ViewerApp.IsWaitingForRestart) {
                UI.ShowWaitOverlay("Computador de volta", "Reconectando a imagem...");
                ViewerApp.IsWaitingForRestart = false;
            }
            ViewerApp.SessionId = newSessionId;
            ViewerApp.AccessKey = newAccessKey;
            this.SendScreenCastRequestToDevice();
        });

        hubConnection.on("Reconnecting", () => {
            if (ViewerApp.IsWaitingForRestart) {
                return;
            }
            UI.SetStatusMessage("Reconectando");
            ShowToast("Reconectando");
        });

        hubConnection.on("CursorChange", (cursor: CursorInfo) => {
            UI.UpdateCursor(cursor.ImageBytes, cursor.HotSpot.X, cursor.HotSpot.Y, cursor.CssOverride);
        });

        hubConnection.on("ShowMessage", (message: string) => {
            if (ViewerApp.IsWaitingForRestart) {
                UI.ShowWaitOverlay("Reiniciando o computador", message);
                return;
            }
            ShowToast(message);
            UI.SetStatusMessage(message);
        });
        hubConnection.on("WindowsSessions", (windowsSessions: Array<WindowsSession>) => {
            UI.UpdateWindowsSessions(windowsSessions);
        });
        hubConnection.on("PingViewer", () => "Pong");
    }
}
