import { ViewerApp } from "./App.js";
import { FolderItemDto, FolderListDto } from "./Interfaces/Dtos.js";
import { FileTransferInput, ShowToast } from "./UI.js";

let pending: ((dto: FolderListDto | null) => void) | null = null;

/** Resposta do computador com as pastas pedidas. */
export function HandleFolderList(dto: FolderListDto) {
    const resolve = pending;
    pending = null;
    resolve?.(dto);
}

function RequestFolders(path: string): Promise<FolderListDto | null> {
    pending?.(null);
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

const icons: Record<string, string> = {
    desktop: "fa-display",
    downloads: "fa-download",
    documents: "fa-file-lines",
    public: "fa-users",
    drive: "fa-hard-drive",
    folder: "fa-folder"
};

function el<K extends keyof HTMLElementTagNameMap>(tag: K, className = "", text = ""): HTMLElementTagNameMap[K] {
    const element = document.createElement(tag);
    if (className) {
        element.className = className;
    }
    if (text) {
        element.textContent = text;
    }
    return element;
}

function icon(kind: string): HTMLElement {
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
    let parent: string | null = null;

    const close = () => {
        pending?.(null);
        backdrop.remove();
    };

    const message = (text: string, kind = "fp-empty") => {
        list.innerHTML = "";
        list.appendChild(el("div", kind, text));
    };

    const open = async (path: string) => {
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

        if (places.childElementCount == 0 && dto.Places?.length) {
            dto.Places.forEach((place: FolderItemDto) => {
                const chip = el("button", "fp-place");
                chip.append(icon(place.Kind), el("span", "", place.Name));
                chip.title = place.Path;
                chip.onclick = () => open(place.Path);
                places.appendChild(chip);
            });
        }

        if (!path && dto.Places?.length) {
            // Começa pela Área de Trabalho do usuário.
            await open(dto.Places[0].Path);
            return;
        }

        current = dto.Path;
        parent = dto.ParentPath || null;
        pathLabel.textContent = current || "Escolha um local acima";
        pathLabel.title = current;
        up.disabled = !parent;
        places.querySelectorAll(".fp-place").forEach((chip: HTMLElement) =>
            chip.classList.toggle("active", chip.title == current));

        list.innerHTML = "";
        if (dto.Error) {
            message(dto.Error, "fp-empty fp-error");
            ok.disabled = !current || dto.Error.indexOf("não encontrada") >= 0;
            return;
        }
        ok.disabled = !current;
        if (!dto.Folders?.length) {
            message("Nenhuma subpasta. Os arquivos vão ser salvos aqui.");
            return;
        }
        dto.Folders.forEach((folder: FolderItemDto) => {
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
