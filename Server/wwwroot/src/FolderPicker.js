import { ViewerApp } from "./App.js";
import { FileTransferInput, ShowToast } from "./UI.js";
let pending = null;
/** Resposta do computador com as pastas pedidas. */
export function HandleFolderList(dto) {
    const resolve = pending;
    pending = null;
    resolve === null || resolve === void 0 ? void 0 : resolve(dto);
}
function RequestFolders(path) {
    pending === null || pending === void 0 ? void 0 : pending(null);
    return new Promise(resolve => {
        pending = resolve;
        window.setTimeout(() => {
            if (pending == resolve) {
                pending = null;
                resolve(null);
            }
        }, 15000);
        ViewerApp.MessageSender.SendFolderListRequest(path);
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
export async function PickFolderAndUpload() {
    const backdrop = el("div", "modal-confirm");
    const card = el("div", "confirm-card folder-picker");
    card.setAttribute("role", "dialog");
    card.setAttribute("aria-modal", "true");
    const heading = el("h3", "", "Enviar arquivos para o computador");
    const intro = el("p", "", "Escolha a pasta onde os arquivos vão ser salvos. Arrastar arquivos para a tela remota também funciona: eles vão para a pasta ou Área de Trabalho onde forem soltos.");
    const places = el("div", "fp-places");
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
    const ok = el("button", "confirm-ok", "Escolher arquivos e enviar aqui");
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
        var _a, _b, _c;
        message("Carregando...");
        ok.disabled = true;
        const dto = await RequestFolders(path);
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
            // Começa pela Área de Trabalho do usuário.
            await open(dto.Places[0].Path);
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
            ok.disabled = !current || dto.Error.indexOf("não encontrada") >= 0;
            return;
        }
        ok.disabled = !current;
        if (!((_c = dto.Folders) === null || _c === void 0 ? void 0 : _c.length)) {
            message("Nenhuma subpasta. Os arquivos vão ser salvos aqui.");
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