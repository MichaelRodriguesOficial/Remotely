import { ViewerApp } from "./App.js";
import { CtrlAltDelDto, KeyDownDto, KeyPressDto, KeyUpDto, MouseDownDto, MouseMoveDto, MouseUpDto, MouseWheelDto, SelectScreenDto, TapDto, ToggleAudioDto, ToggleBlockInputDto, TextTransferDto, FileDto, WindowsSessionsDto, EmptyDto, FrameReceivedDto, FolderListRequestDto, FileListDto } from "./Interfaces/Dtos.js";
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
    async ChangeWindowsSession(sessionId) {
        await ViewerApp.ViewerHubConnection.ChangeWindowsSession(sessionId);
    }
    async SendFrameReceived(timestamp) {
        var dto = new FrameReceivedDto(timestamp);
        await ViewerApp.ViewerHubConnection.SendDtoToClient(dto, DtoType.FrameReceived);
    }
    async SendSelectScreen(displayName) {
        var dto = new SelectScreenDto(displayName);
        await ViewerApp.ViewerHubConnection.SendDtoToClient(dto, DtoType.SelectScreen);
    }
    async SendMouseMove(percentX, percentY) {
        var dto = new MouseMoveDto(percentX, percentY);
        await ViewerApp.ViewerHubConnection.SendDtoToClient(dto, DtoType.MouseMove);
    }
    async SendMouseDown(button, percentX, percentY) {
        var dto = new MouseDownDto(button, percentX, percentY);
        await ViewerApp.ViewerHubConnection.SendDtoToClient(dto, DtoType.MouseDown);
    }
    async SendMouseUp(button, percentX, percentY) {
        var dto = new MouseUpDto(button, percentX, percentY);
        await ViewerApp.ViewerHubConnection.SendDtoToClient(dto, DtoType.MouseUp);
    }
    async SendTap(percentX, percentY) {
        var dto = new TapDto(percentX, percentY);
        await ViewerApp.ViewerHubConnection.SendDtoToClient(dto, DtoType.Tap);
    }
    async SendMouseWheel(deltaX, deltaY) {
        var dto = new MouseWheelDto(deltaX, deltaY);
        await ViewerApp.ViewerHubConnection.SendDtoToClient(dto, DtoType.MouseWheel);
    }
    async SendKeyDown(key) {
        var dto = new KeyDownDto(key);
        await ViewerApp.ViewerHubConnection.SendDtoToClient(dto, DtoType.KeyDown);
    }
    async SendKeyUp(key) {
        var dto = new KeyUpDto(key);
        await ViewerApp.ViewerHubConnection.SendDtoToClient(dto, DtoType.KeyUp);
    }
    async SendKeyPress(key) {
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
    async SendFile(buffer, fileName, destination = null, batch = null, isCanceled = () => false) {
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
        for (var i = 0; i < buffer.byteLength; i += 50000) {
            if (isCanceled()) {
                const cancel = new FileDto(null, fileName, messageId, true, false);
                cancel.Canceled = true;
                cancel.BatchId = batch === null || batch === void 0 ? void 0 : batch.Id;
                await ViewerApp.ViewerHubConnection.SendDtoToClient(cancel, DtoType.File);
                return false;
            }
            let dto = new FileDto(buffer.slice(i, i + 50000), fileName, messageId, false, false);
            await ViewerApp.ViewerHubConnection.SendDtoToClient(dto, DtoType.File);
            FileTransferProgress.value = total > 0 ? (before + i + 50000) / total : 1;
        }
        dto = new FileDto(null, fileName, messageId, true, false);
        await ViewerApp.ViewerHubConnection.SendDtoToClient(dto, DtoType.File);
        return true;
    }
    async SendDownloadRequest(paths) {
        const dto = new FileListDto(paths.map(x => ({ Name: "", Path: x, Size: 0 })));
        await ViewerApp.ViewerHubConnection.SendDtoToClient(dto, DtoType.DownloadFilesRequest);
    }
    async SendToggleAudio(toggleOn) {
        var dto = new ToggleAudioDto(toggleOn);
        await ViewerApp.ViewerHubConnection.SendDtoToClient(dto, DtoType.ToggleAudio);
    }
    ;
    async SendToggleBlockInput(toggleOn) {
        var dto = new ToggleBlockInputDto(toggleOn);
        await ViewerApp.ViewerHubConnection.SendDtoToClient(dto, DtoType.ToggleBlockInput);
    }
    async SendFolderListRequest(path, includeFiles = false) {
        await ViewerApp.ViewerHubConnection.SendDtoToClient(new FolderListRequestDto(path, includeFiles), DtoType.FolderListRequest);
    }
    async SendTextTransfer(text, typeText) {
        var dto = new TextTransferDto(text, typeText);
        await ViewerApp.ViewerHubConnection.SendDtoToClient(dto, DtoType.TextTransfer);
    }
}
//# sourceMappingURL=MessageSender.js.map