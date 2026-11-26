import { FileTransferProgress, FileTransferInput, FileTransferNameSpan } from "./UI.js";
import { ViewerApp } from "./App.js";
import { ShowToast } from "./UI.js";
const PartialDownloads = {};
/**
 * Envia arquivos ao computador remoto. destination: "drop:x;y" (soltos na tela: a pasta sob o ponto),
 * "path:C:\\pasta" (escolhida no visualizador) ou vazio (Área de Trabalho do usuário).
 */
export async function UploadFiles(fileList, destination = null) {
    if (!FileTransferProgress.parentElement.hasAttribute("hidden")) {
        FileTransferInput.value = null;
        ShowToast("Já há uma transferência de arquivo em andamento.");
        return;
    }
    ShowToast("Envio do arquivo iniciado");
    FileTransferProgress.value = 0;
    FileTransferProgress.parentElement.removeAttribute("hidden");
    try {
        for (var i = 0; i < fileList.length; i++) {
            FileTransferNameSpan.textContent = fileList[i].name;
            var buffer = await fileList[i].arrayBuffer();
            await ViewerApp.MessageSender.SendFile(new Uint8Array(buffer), fileList[i].name, destination);
        }
    }
    catch (_a) {
        ShowToast("Falha no envio do arquivo.");
    }
    FileTransferInput.value = null;
    FileTransferProgress.parentElement.setAttribute("hidden", "hidden");
}
export async function ReceiveFile(file) {
    if (file.StartOfFile) {
        ShowToast(`Baixando o arquivo ${file.FileName}`);
    }
    var partial = PartialDownloads[file.MessageId];
    if (!partial) {
        partial = new Array();
        PartialDownloads[file.MessageId] = partial;
    }
    if (file.Buffer) {
        partial.push(file.Buffer);
    }
    if (file.EndOfFile) {
        var blob = new Blob(partial, { type: 'application/octet-stream' });
        var url = window.URL.createObjectURL(blob);
        var link = document.createElement('a');
        link.style.display = 'none';
        link.href = url;
        link.download = file.FileName;
        document.body.appendChild(link);
        link.click();
        setTimeout(() => {
            document.body.removeChild(link);
            window.URL.revokeObjectURL(url);
        }, 100);
    }
}
/** O computador avisou onde salvou. */
export function HandleFileSaved(dto) {
    if (dto.Success) {
        ShowToast(`${dto.FileName} salvo em ${dto.FullPath}`, 9000);
    }
    else {
        ShowToast(dto.Message || `Não foi possível salvar ${dto.FileName}.`);
    }
}
//# sourceMappingURL=FileTransferService.js.map