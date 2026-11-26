import { ViewerApp } from "./App.js";
import {
    CtrlAltDelDto,
    KeyDownDto,
    KeyPressDto,
    KeyUpDto,
    MouseDownDto,
    MouseMoveDto,
    MouseUpDto,
    MouseWheelDto,
    SelectScreenDto,
    TapDto,
    ToggleAudioDto,
    ToggleBlockInputDto,
    TextTransferDto,
    FileDto,
    WindowsSessionsDto,
    DtoWrapper,
    EmptyDto,
    FrameReceivedDto,
    FolderListRequestDto,
    FileListDto
} from "./Interfaces/Dtos.js";
import { CreateGUID } from "./Utilities.js";
import { FileTransferProgress } from "./UI.js";
import { DtoType } from "./Enums/DtoType.js";
import { RemoteControlMode } from "./Enums/RemoteControlMode.js";

export class MessageSender {
    async GetWindowsSessions() {
        if (ViewerApp.Mode == RemoteControlMode.Unattended) {
            var dto = new WindowsSessionsDto();
            await ViewerApp.ViewerHubConnection.SendDtoToClient(dto, DtoType.WindowsSessions);
        }
    }
    async ChangeWindowsSession(sessionId: number) {
        await ViewerApp.ViewerHubConnection.ChangeWindowsSession(sessionId);
    }
    async SendFrameReceived(timestamp: number) {
        var dto = new FrameReceivedDto(timestamp);
        await ViewerApp.ViewerHubConnection.SendDtoToClient(dto, DtoType.FrameReceived);
    }
    async SendSelectScreen(displayName: string) {
        var dto = new SelectScreenDto(displayName);
        await ViewerApp.ViewerHubConnection.SendDtoToClient(dto, DtoType.SelectScreen);
    }
    async SendMouseMove(percentX: number, percentY: number) {
        var dto = new MouseMoveDto(percentX, percentY);
        await ViewerApp.ViewerHubConnection.SendDtoToClient(dto, DtoType.MouseMove);
    }
    async SendMouseDown(button: number, percentX: number, percentY: number) {
        var dto = new MouseDownDto(button, percentX, percentY);
        await ViewerApp.ViewerHubConnection.SendDtoToClient(dto, DtoType.MouseDown);
    }
    async SendMouseUp(button: number, percentX: number, percentY: number) {
        var dto = new MouseUpDto(button, percentX, percentY);
        await ViewerApp.ViewerHubConnection.SendDtoToClient(dto, DtoType.MouseUp);
    }
    async SendTap(percentX: number, percentY: number) {
        var dto = new TapDto(percentX, percentY);
        await ViewerApp.ViewerHubConnection.SendDtoToClient(dto, DtoType.Tap);
    }
    async SendMouseWheel(deltaX: number, deltaY: number) {
        var dto = new MouseWheelDto(deltaX, deltaY);
        await ViewerApp.ViewerHubConnection.SendDtoToClient(dto, DtoType.MouseWheel);
    }
    async SendKeyDown(key: string) {
        var dto = new KeyDownDto(key);
        await ViewerApp.ViewerHubConnection.SendDtoToClient(dto, DtoType.KeyDown);
    }
    async SendKeyUp(key: string) {
        var dto = new KeyUpDto(key);
        await ViewerApp.ViewerHubConnection.SendDtoToClient(dto, DtoType.KeyUp);
    }
    async SendKeyPress(key: string) {
        var dto = new KeyPressDto(key);
        await ViewerApp.ViewerHubConnection.SendDtoToClient(dto, DtoType.KeyPress);
    }
    async SendSetKeyStatesUp() {
        var dto = new EmptyDto();
        await ViewerApp.ViewerHubConnection.SendDtoToClient(dto, DtoType.SetKeyStatesUp);
    }
    async SendCtrlAltDel() {
        var dto = new CtrlAltDelDto();
        await ViewerApp.ViewerHubConnection.SendDtoToClient(dto, DtoType.CtrlAltDel);
        await ViewerApp.ViewerHubConnection.InvokeCtrlAltDel();
    }

    async SendOpenFileTransferWindow() {
        var dto = new EmptyDto();
        await ViewerApp.ViewerHubConnection.SendDtoToClient(dto, DtoType.OpenFileTransferWindow);
    }

    /**
     * Envia um arquivo em pedaços. batch: o lote (vários arquivos juntos) para a janela de progresso
     * na tela do usuário. isCanceled: o usuário ou o técnico cancelou; para de enviar.
     */
    async SendFile(
        buffer: Uint8Array,
        fileName: string,
        destination: string = null,
        batch: { Id: string, TotalBytes: number, Count: number, SentBefore: number } = null,
        isCanceled: () => boolean = () => false): Promise<boolean> {
        var messageId = CreateGUID();
        let dto = new FileDto(null, fileName, messageId, false, true, destination);
        dto.FileSize = buffer.byteLength;
        if (batch) {
            dto.BatchId = batch.Id;
            dto.BatchTotalBytes = batch.TotalBytes;
            dto.BatchFileCount = batch.Count;
        }
        await ViewerApp.ViewerHubConnection.SendDtoToClient(dto, DtoType.File);

        const total = batch ? batch.TotalBytes : buffer.byteLength;
        const before = batch ? batch.SentBefore : 0;

        for (var i = 0; i < buffer.byteLength; i += 50_000) {
            if (isCanceled()) {
                const cancel = new FileDto(null, fileName, messageId, true, false);
                cancel.Canceled = true;
                cancel.BatchId = batch?.Id;
                await ViewerApp.ViewerHubConnection.SendDtoToClient(cancel, DtoType.File);
                return false;
            }

            let dto = new FileDto(buffer.slice(i, i + 50_000), fileName, messageId, false, false);

            await ViewerApp.ViewerHubConnection.SendDtoToClient(dto, DtoType.File);

            FileTransferProgress.value = total > 0 ? (before + i + 50_000) / total : 1;
        }

        dto = new FileDto(null, fileName, messageId, true, false);

        await ViewerApp.ViewerHubConnection.SendDtoToClient(dto, DtoType.File);
        return true;
    }

    async SendDownloadRequest(paths: string[]) {
        const dto = new FileListDto(paths.map(x => ({ Name: "", Path: x, Size: 0 })));
        await ViewerApp.ViewerHubConnection.SendDtoToClient(dto, DtoType.DownloadFilesRequest);
    }

    async SendToggleAudio(toggleOn: boolean) {
        var dto = new ToggleAudioDto(toggleOn);
        await ViewerApp.ViewerHubConnection.SendDtoToClient(dto, DtoType.ToggleAudio);

    };
    async SendToggleBlockInput(toggleOn: boolean) {
        var dto = new ToggleBlockInputDto(toggleOn);
        await ViewerApp.ViewerHubConnection.SendDtoToClient(dto, DtoType.ToggleBlockInput);

    }

    async SendFolderListRequest(path: string, includeFiles = false) {
        await ViewerApp.ViewerHubConnection.SendDtoToClient(new FolderListRequestDto(path, includeFiles), DtoType.FolderListRequest);
    }

    async SendTextTransfer(text: string, typeText: boolean) {
        var dto = new TextTransferDto(text, typeText);
        await ViewerApp.ViewerHubConnection.SendDtoToClient(dto, DtoType.TextTransfer);

    }
}
