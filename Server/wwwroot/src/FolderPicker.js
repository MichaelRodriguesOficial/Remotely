import { ViewerApp } from "./App.js";
import { FileTransferInput, ShowToast } from "./UI.js";
import { FormatSize } from "./FileTransferService.js";
let pending = null;
/** Resposta do computador com as pastas pedidas. */
export function HandleFolderList(dto) {
    const resolve = pending;
    pending = null;
    resolve === null || resolve === void 0 ? void 0 : resolve(dto);
}
function RequestFolders(path, includeFiles = false) {
    pending === null || pending === void 0 ? void 0 : pending(null);
    return new Promise(resolve => {
        pending = resolve;
        window.setTimeout(() => {
            if (pending == resolve) {
                pending = null;
                resolve(null);
            }
        }, 15000);
        ViewerApp.MessageSender.SendFolderListRequest(path, includeFiles);
    });
}
const icons = {
    desktop: "fa-display",
    downloads: "fa-download",
    documents: "fa-file-lines",
    public: "fa-users",
    drive: "fa-hard-drive",
    folder: "fa-folder"
};
function el(tag, className = "", text = "") {
    const element = document.createElement(tag);
    if (className) {
        element.className = className;
    }
    if (text) {
        element.textContent = text;
    }
    return element;
}
function icon(kind) {
    const i = el("i", `fas ${icons[kind] || icons.folder}`);
    i.setAttribute("aria-hidden", "true");
    return i;
}
/**
 * "Enviar arquivo": mostra as pastas do computador remoto para escolher onde salvar,
 * depois abre a escolha de arquivos e envia para lá.
 */
export function PickFolderAndUpload() {
    return OpenPicker("upload");
}
/** "Baixar arquivos": navega pelas pastas do computador remoto e escolhe os arquivos. */
export function PickFilesAndDownload() {
    return OpenPicker("download");
}
async function OpenPicker(mode) {
    const download = mode == "download";
    const selected = new Map();
    const backdrop = el("div", "modal-confirm");
    const card = el("div", "confirm-card folder-picker");
    card.setAttribute("role", "dialog");
    card.setAttribute("aria-modal", "true");
    const heading = el("h3", "", download ? "Baixar arquivos do computador" : "Enviar arquivos para o computador");
    const intro = el("p", "", download
        ? "Marque os arquivos e clique em Baixar. O usuário vê uma janela com o progresso e pode cancelar."
        : "Escolha a pasta onde os arquivos vão ser salvos. Arrastar arquivos para a tela remota ou colar com Ctrl+V também funciona.");
    const places = el("div", "fp-places");
    if (download) {
        places.classList.add("d-none");
    }
    const bar = el("div", "fp-bar");
    const up = el("button", "fp-up");
    up.title = "Pasta acima";
    up.appendChild(icon("up"));
    up.querySelector("i").className = "fas fa-arrow-up";
    const pathLabel = el("span", "fp-path", "Carregando...");
    bar.append(up, pathLabel);
    const list = el("div", "fp-list");
    list.setAttribute("role", "listbox");
    const actions = el("div", "confirm-actions");
    const cancel = el("button", "", "Cancelar");
    const ok = el("button", "confirm-ok", download ? "Baixar" : "Escolher arquivos e enviar aqui");
    const updateOk = () => {
        if (!download) {
            return;
        }
        const total = Array.from(selected.values()).reduce((sum, x) => sum + (x.Size || 0), 0);
        ok.textContent = selected.size == 0 ? "Baixar" : `Baixar ${selected.size} arquivo(s) · ${FormatSize(total)}`;
        ok.disabled = selected.size == 0;
    };
    ok.disabled = true;
    actions.append(cancel, ok);
    card.append(heading, intro, places, bar, list, actions);
    backdrop.appendChild(card);
    document.body.appendChild(backdrop);
    let current = "";
    let parent = null;
    const close = () => {
        pending === null || pending === void 0 ? void 0 : pending(null);
        backdrop.remove();
    };
    const message = (text, kind = "fp-empty") => {
        list.innerHTML = "";
        list.appendChild(el("div", kind, text));
    };
    const open = async (path) => {
        var _a, _b, _c, _d;
        message("Carregando...");
        ok.disabled = true;
        const dto = await RequestFolders(path, download);
        if (!document.body.contains(backdrop)) {
            return;
        }
        if (!dto) {
            message("O computador não respondeu. Tente de novo.", "fp-empty fp-error");
            return;
        }
        if (places.childElementCount == 0 && ((_a = dto.Places) === null || _a === void 0 ? void 0 : _a.length)) {
            dto.Places.forEach((place) => {
                const chip = el("button", "fp-place");
                chip.append(icon(place.Kind), el("span", "", place.Name));
                chip.title = place.Path;
                chip.onclick = () => open(place.Path);
                places.appendChild(chip);
            });
        }
        if (!path && ((_b = dto.Places) === null || _b === void 0 ? void 0 : _b.length)) {
            // Enviar: começa pela Área de Trabalho do usuário. Baixar: pelo disco C:.
            const start = download
                ? (dto.Places.find(x => x.Kind == "drive" && /^c:/i.test(x.Path)) || dto.Places.find(x => x.Kind == "drive") || dto.Places[0])
                : dto.Places[0];
            await open(start.Path);
            return;
        }
        current = dto.Path;
        parent = dto.ParentPath || null;
        pathLabel.textContent = current || "Escolha um local acima";
        pathLabel.title = current;
        up.disabled = !parent;
        places.querySelectorAll(".fp-place").forEach((chip) => chip.classList.toggle("active", chip.title == current));
        list.innerHTML = "";
        if (dto.Error) {
            message(dto.Error, "fp-empty fp-error");
            ok.disabled = download ? selected.size == 0 : (!current || dto.Error.indexOf("não encontrada") >= 0);
            return;
        }
        ok.disabled = !current;
        updateOk();
        if (!((_c = dto.Folders) === null || _c === void 0 ? void 0 : _c.length) && !(download && ((_d = dto.Files) === null || _d === void 0 ? void 0 : _d.length))) {
            message(download ? "Pasta vazia." : "Nenhuma subpasta. Os arquivos vão ser salvos aqui.");
            return;
        }
        dto.Folders.forEach((folder) => {
            const item = el("button", "fp-item");
            item.setAttribute("role", "option");
            item.append(icon("folder"), el("span", "", folder.Name));
            item.title = folder.Path;
            item.onclick = () => open(folder.Path);
            list.appendChild(item);
        });
        if (download) {
            (dto.Files || []).forEach((file) => {
                const item = el("button", "fp-item fp-file");
                item.setAttribute("role", "option");
                const check = el("i", selected.has(file.Path) ? "fas fa-square-check" : "far fa-square");
                const name = el("span", "", file.Name);
                const meta = el("small", "", `${FormatSize(file.Size)}${file.Modified ? " · " + file.Modified : ""}`);
                item.append(check, name, meta);
                item.title = file.Path;
                item.classList.toggle("selected", selected.has(file.Path));
                item.onclick = () => {
                    if (selected.has(file.Path)) {
                        selected.delete(file.Path);
                    }
                    else {
                        selected.set(file.Path, file);
                    }
                    const on = selected.has(file.Path);
                    item.classList.toggle("selected", on);
                    check.className = on ? "fas fa-square-check" : "far fa-square";
                    updateOk();
                };
                list.appendChild(item);
            });
        }
    };
    up.onclick = () => parent && open(parent);
    cancel.onclick = close;
    backdrop.onclick = ev => {
        if (ev.target == backdrop) {
            close();
        }
    };
    card.onkeydown = ev => {
        ev.stopPropagation();
        if (ev.key == "Escape") {
            close();
        }
    };
    card.onkeyup = ev => ev.stopPropagation();
    ok.onclick = () => {
        if (download) {
            if (selected.size == 0) {
                return;
            }
            const paths = Array.from(selected.keys());
            close();
            ViewerApp.MessageSender.SendDownloadRequest(paths);
            ShowToast(`Baixando ${paths.length} arquivo(s). O navegador salva na sua pasta Downloads.`, 8000);
            return;
        }
        if (!current) {
            return;
        }
        ViewerApp.PendingUploadDestination = `path:${current}`;
        close();
        FileTransferInput.click();
        ShowToast(`Os arquivos vão ser salvos em ${current}`);
    };
    await open("");
}
//# sourceMappingURL=FolderPicker.js.map