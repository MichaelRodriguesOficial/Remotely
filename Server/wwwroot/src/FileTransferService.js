import { FileTransferProgress, FileTransferInput, FileTransferNameSpan, ToastsWrapper } from "./UI.js";
import { ViewerApp } from "./App.js";
import { ShowToast } from "./UI.js";
import { CreateGUID } from "./Utilities.js";
const PartialDownloads = {};
const canceledBatches = new Set();
let currentBatch = null;
/** Técnico clicou em Cancelar na barra de envio. */
export function CancelCurrentUpload() {
    if (currentBatch) {
        canceledBatches.add(currentBatch);
        ShowToast("Cancelando o envio...");
    }
}
/**
 * Envia arquivos ao computador remoto. destination: "drop:x;y" (soltos na tela: a pasta sob o ponto),
 * "paste" (Ctrl+V: a pasta da janela em primeiro plano), "path:C:\\pasta" (escolhida) ou vazio
 * (Área de Trabalho do usuário). O usuário vê uma janela com o progresso e pode cancelar.
 */
export async function UploadFiles(fileList, destination = null) {
    if (!FileTransferProgress.parentElement.hasAttribute("hidden")) {
        FileTransferInput.value = null;
        ShowToast("Já há uma transferência de arquivo em andamento.");
        return;
    }
    const files = Array.from(fileList);
    if (files.length == 0) {
        return;
    }
    const batch = { Id: CreateGUID(), TotalBytes: files.reduce((sum, f) => sum + f.size, 0), Count: files.length, SentBefore: 0 };
    currentBatch = batch.Id;
    FileTransferProgress.value = 0;
    FileTransferProgress.parentElement.removeAttribute("hidden");
    try {
        for (const file of files) {
            if (canceledBatches.has(batch.Id)) {
                break;
            }
            FileTransferNameSpan.textContent = files.length > 1 ? `${file.name} (${files.indexOf(file) + 1} de ${files.length})` : file.name;
            const buffer = await file.arrayBuffer();
            const sent = await ViewerApp.MessageSender.SendFile(new Uint8Array(buffer), file.name, destination, batch, () => canceledBatches.has(batch.Id));
            if (!sent) {
                break;
            }
            batch.SentBefore += file.size;
        }
    }
    catch (_a) {
        ShowToast("Falha no envio do arquivo.");
    }
    currentBatch = null;
    FileTransferInput.value = null;
    FileTransferProgress.parentElement.setAttribute("hidden", "hidden");
}
export async function ReceiveFile(file) {
    if (file.StartOfFile) {
        ShowToast(`Baixando ${file.FileName}...`);
    }
    var partial = PartialDownloads[file.MessageId];
    if (!partial) {
        partial = new Array();
        PartialDownloads[file.MessageId] = partial;
    }
    if (file.Canceled) {
        delete PartialDownloads[file.MessageId];
        return;
    }
    if (file.Buffer) {
        partial.push(file.Buffer);
    }
    if (file.EndOfFile) {
        delete PartialDownloads[file.MessageId];
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
/** O computador avisou onde salvou (ou que o usuário cancelou). */
export function HandleFileSaved(dto) {
    if (dto.Canceled) {
        if (dto.BatchId) {
            canceledBatches.add(dto.BatchId);
        }
        ShowToast(dto.Message || "Transferência cancelada.", 8000);
        return;
    }
    if (dto.Success) {
        ShowToast(`${dto.FileName} salvo em ${dto.FullPath}`, 9000);
    }
    else {
        ShowToast(dto.Message || `Não foi possível salvar ${dto.FileName}.`);
    }
}
function FormatSize(bytes) {
    if (bytes < 1024)
        return `${bytes} B`;
    if (bytes < 1024 * 1024)
        return `${(bytes / 1024).toFixed(1).replace(".", ",")} KB`;
    if (bytes < 1024 * 1024 * 1024)
        return `${(bytes / 1024 / 1024).toFixed(1).replace(".", ",")} MB`;
    return `${(bytes / 1024 / 1024 / 1024).toFixed(2).replace(".", ",")} GB`;
}
export { FormatSize };
let clipboardCard = null;
/** O usuário copiou arquivos lá: oferece baixar (o navegador não deixa colocar arquivos no Ctrl+V daqui). */
export function HandleClipboardFiles(dto) {
    var _a, _b;
    if (!((_a = dto.Files) === null || _a === void 0 ? void 0 : _a.length) || ViewerApp.Settings.ClipboardSync === false || ((_b = ViewerApp.Settings.ViewerOptions) === null || _b === void 0 ? void 0 : _b.AllowFiles) === false) {
        return;
    }
    clipboardCard === null || clipboardCard === void 0 ? void 0 : clipboardCard.remove();
    const card = document.createElement("div");
    card.className = "clipboard-files-card";
    const text = document.createElement("div");
    const names = dto.Files.slice(0, 3).map((x) => x.Name).join(", ") + (dto.Files.length > 3 ? ` e mais ${dto.Files.length - 3}` : "");
    const total = dto.Files.reduce((sum, x) => sum + (x.Size || 0), 0);
    const strong = document.createElement("strong");
    strong.textContent = dto.Files.length == 1 ? "Arquivo copiado no computador" : `${dto.Files.length} arquivos copiados no computador`;
    const detail = document.createElement("span");
    detail.textContent = `${names} · ${FormatSize(total)}`;
    text.append(strong, detail);
    const download = document.createElement("button");
    download.className = "cf-download";
    download.innerHTML = '<i class="fas fa-download"></i> Baixar';
    const close = document.createElement("button");
    close.className = "cf-close";
    close.title = "Dispensar";
    close.innerHTML = '<i class="fas fa-xmark"></i>';
    card.append(text, download, close);
    ToastsWrapper.parentElement.appendChild(card);
    clipboardCard = card;
    download.onclick = async () => {
        card.remove();
        await ViewerApp.MessageSender.SendDownloadRequest(dto.Files.map((x) => x.Path));
        ShowToast("Baixando os arquivos copiados...");
    };
    close.onclick = () => card.remove();
    window.setTimeout(() => card.remove(), 30000);
}
//# sourceMappingURL=FileTransferService.js.map