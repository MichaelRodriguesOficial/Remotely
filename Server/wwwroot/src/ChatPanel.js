import { ViewerApp } from "./App.js";
import { DtoType } from "./Enums/DtoType.js";
import { ShowToast } from "./UI.js";
import { CreateGUID } from "./Utilities.js";
/** Tamanho máximo de cada anexo (igual ao do computador remoto). */
const MaxAttachmentBytes = 25 * 1024 * 1024;
const MaxAttachments = 10;
const MaxText = 4000;
const EmojiGroups = [
    ["smileys", "😀", ["😀", "😃", "😄", "😁", "😆", "😅", "😂", "🤣", "😊", "🙂", "😉", "😍", "😘", "😎", "🤓", "🤔", "🤨", "😐", "😑", "🙄", "😏", "😬", "😴", "😌", "😕", "🙁", "😟", "😢", "😭", "😤", "😡", "🤯", "😳", "😱", "🥲", "🥳", "🤗", "🤝", "🫡", "😇"]],
    ["gestos", "👍", ["👍", "👎", "👌", "✌️", "🤞", "🙏", "👏", "🙌", "👋", "✋", "👉", "👈", "👆", "👇", "💪", "🫶", "❤️", "💙", "💚", "💜", "🔥", "✨", "⭐", "💯", "✅", "❌", "⚠️", "❓", "❗", "💡"]],
    ["ti", "💻", ["💻", "🖥️", "⌨️", "🖱️", "🖨️", "📱", "📞", "☎️", "🔌", "🔋", "💾", "📀", "🧰", "🛠️", "🔧", "🔨", "⚙️", "🔒", "🔓", "🔑", "🛡️", "🌐", "📶", "📡", "🐛", "🚀", "📦", "🗑️", "♻️", "⏳"]],
    ["trabalho", "📄", ["📄", "📃", "📑", "📊", "📈", "📉", "📋", "📌", "📎", "🖇️", "📁", "📂", "🗂️", "📅", "🗓️", "⏰", "⏱️", "✉️", "📧", "📨", "📝", "✏️", "🖊️", "🔍", "💬", "🗨️", "☕", "🍕", "🎉", "🏁"]]
];
let panel;
let list;
let input;
let pendingStrip;
let typingLabel;
let badge;
let button;
let picker;
let fileInput;
let pending = [];
let unread = 0;
let lastIncomingId = null;
let lastTypingSent = 0;
let typingTimer = 0;
let initialized = false;
const ownMessages = new Map();
export function InitChatPanel() {
    var _a, _b, _c, _d;
    if (initialized) {
        return;
    }
    panel = document.getElementById("chatPanel");
    button = document.getElementById("chatButton");
    if (!panel || !button) {
        return;
    }
    initialized = true;
    list = document.getElementById("chatMessages");
    input = document.getElementById("chatInput");
    pendingStrip = document.getElementById("chatPending");
    typingLabel = document.getElementById("chatTyping");
    badge = document.getElementById("chatBadge");
    picker = document.getElementById("chatEmojiPicker");
    fileInput = document.getElementById("chatFileInput");
    BuildEmojiPicker();
    button.addEventListener("click", ev => {
        ev.stopPropagation();
        ToggleChatPanel(panel.classList.contains("d-none"));
    });
    (_a = document.getElementById("chatClose")) === null || _a === void 0 ? void 0 : _a.addEventListener("click", () => ToggleChatPanel(false));
    (_b = document.getElementById("chatSend")) === null || _b === void 0 ? void 0 : _b.addEventListener("click", () => SendCurrent());
    (_c = document.getElementById("chatAttach")) === null || _c === void 0 ? void 0 : _c.addEventListener("click", () => {
        if (!CanSendFiles()) {
            ShowToast("Seu perfil não permite enviar arquivos.");
            return;
        }
        fileInput.click();
    });
    (_d = document.getElementById("chatEmoji")) === null || _d === void 0 ? void 0 : _d.addEventListener("click", ev => {
        ev.stopPropagation();
        picker.classList.toggle("d-none");
    });
    fileInput.addEventListener("change", () => {
        AddFiles(Array.from(fileInput.files || []));
        fileInput.value = "";
    });
    input.addEventListener("keydown", ev => {
        ev.stopPropagation();
        if (ev.key == "Enter" && !ev.shiftKey && !ev.isComposing) {
            ev.preventDefault();
            SendCurrent();
        }
        else if (ev.key == "Escape") {
            picker.classList.add("d-none");
        }
    });
    input.addEventListener("keyup", ev => ev.stopPropagation());
    input.addEventListener("input", () => {
        AutoGrow();
        SendTyping();
    });
    // Ctrl+V no campo do chat: imagens e arquivos viram anexos (não vão para a tela remota).
    input.addEventListener("paste", ev => {
        var _a;
        ev.stopPropagation();
        const files = Array.from(((_a = ev.clipboardData) === null || _a === void 0 ? void 0 : _a.files) || []);
        if (files.length > 0) {
            ev.preventDefault();
            AddFiles(files);
        }
    });
    // Arrastar para o chat anexa (em vez de enviar para a pasta do computador remoto).
    panel.addEventListener("dragover", ev => {
        ev.preventDefault();
        ev.stopPropagation();
        panel.classList.add("dragging");
    });
    panel.addEventListener("dragleave", ev => {
        if (ev.target == panel) {
            panel.classList.remove("dragging");
        }
    });
    panel.addEventListener("drop", ev => {
        var _a;
        ev.preventDefault();
        ev.stopPropagation();
        panel.classList.remove("dragging");
        AddFiles(Array.from(((_a = ev.dataTransfer) === null || _a === void 0 ? void 0 : _a.files) || []));
    });
    panel.addEventListener("click", ev => {
        if (!picker.contains(ev.target) && ev.target.closest("#chatEmoji") == null) {
            picker.classList.add("d-none");
        }
    });
}
export function ToggleChatPanel(show) {
    var _a, _b;
    if (!initialized) {
        return;
    }
    panel.classList.toggle("d-none", !show);
    button.classList.toggle("toggled", show);
    button.setAttribute("aria-expanded", String(show));
    if (show) {
        (_a = document.getElementById("sidePanel")) === null || _a === void 0 ? void 0 : _a.classList.add("d-none");
        (_b = document.getElementById("panelButton")) === null || _b === void 0 ? void 0 : _b.classList.remove("toggled");
        unread = 0;
        UpdateBadge();
        SendRead();
        window.setTimeout(() => input.focus(), 50);
        ScrollToEnd();
    }
    else {
        picker.classList.add("d-none");
    }
}
/** Mensagem do usuário do computador remoto. */
export function ReceiveChatMessage(dto) {
    var _a, _b;
    if (!initialized || !dto) {
        return;
    }
    typingLabel.classList.add("d-none");
    lastIncomingId = dto.Id;
    (_a = list.querySelector(".chat-empty")) === null || _a === void 0 ? void 0 : _a.remove();
    list.appendChild(RenderMessage(dto, false));
    ScrollToEnd();
    if (panel.classList.contains("d-none") || document.hidden) {
        unread++;
        UpdateBadge();
        PlayBeep();
        const preview = dto.Text ? dto.Text.slice(0, 80) : `${((_b = dto.Attachments) === null || _b === void 0 ? void 0 : _b.length) || 0} anexo(s)`;
        ShowToast(`💬 ${dto.SenderName || "Usuário"}: ${preview}`, 6000);
    }
    else {
        SendRead();
    }
}
export function ReceiveChatTyping(dto) {
    if (!initialized) {
        return;
    }
    typingLabel.textContent = `${(dto === null || dto === void 0 ? void 0 : dto.SenderName) || "Usuário"} está digitando...`;
    typingLabel.classList.remove("d-none");
    window.clearTimeout(typingTimer);
    typingTimer = window.setTimeout(() => typingLabel.classList.add("d-none"), 4500);
}
/** Confirmação de leitura: marca as mensagens enviadas até MessageId. */
export function ReceiveChatRead(dto) {
    var _a;
    if (!initialized || !(dto === null || dto === void 0 ? void 0 : dto.MessageId)) {
        return;
    }
    let reached = false;
    const ids = Array.from(ownMessages.keys());
    for (let i = ids.length - 1; i >= 0; i--) {
        if (ids[i] == dto.MessageId) {
            reached = true;
        }
        if (reached) {
            const status = (_a = ownMessages.get(ids[i])) === null || _a === void 0 ? void 0 : _a.querySelector(".chat-status");
            if (status) {
                status.classList.add("read");
                status.setAttribute("title", "Lida");
            }
        }
    }
}
function CanSendFiles() {
    var _a;
    return ((_a = ViewerApp.Options) === null || _a === void 0 ? void 0 : _a.AllowFiles) !== false;
}
function AddFiles(files) {
    if (files.length == 0) {
        return;
    }
    if (!CanSendFiles()) {
        ShowToast("Seu perfil não permite enviar arquivos.");
        return;
    }
    for (const file of files) {
        if (pending.length >= MaxAttachments) {
            ShowToast(`Até ${MaxAttachments} anexos por mensagem.`);
            break;
        }
        if (file.size > MaxAttachmentBytes) {
            ShowToast(`${file.name} passa de 25 MB. Use Arquivos → Enviar.`);
            continue;
        }
        const isImage = file.type.startsWith("image/") && !file.type.includes("svg");
        pending.push({ file, url: isImage ? URL.createObjectURL(file) : null });
    }
    RenderPending();
    input.focus();
}
function RenderPending() {
    pendingStrip.innerHTML = "";
    pendingStrip.classList.toggle("d-none", pending.length == 0);
    pending.forEach((item, index) => {
        const chip = document.createElement("div");
        chip.className = "chat-pending-item";
        if (item.url) {
            const img = document.createElement("img");
            img.src = item.url;
            img.alt = item.file.name;
            chip.appendChild(img);
        }
        else {
            const icon = document.createElement("i");
            icon.className = `fas ${FileIcon(item.file.name)}`;
            chip.appendChild(icon);
        }
        const name = document.createElement("span");
        name.textContent = item.file.name;
        chip.appendChild(name);
        const remove = document.createElement("button");
        remove.type = "button";
        remove.title = "Remover";
        remove.innerHTML = '<i class="fas fa-xmark"></i>';
        remove.onclick = () => {
            if (item.url) {
                URL.revokeObjectURL(item.url);
            }
            pending.splice(index, 1);
            RenderPending();
        };
        chip.appendChild(remove);
        pendingStrip.appendChild(chip);
    });
}
async function SendCurrent() {
    var _a;
    const text = input.value.trim().slice(0, MaxText);
    if (!text && pending.length == 0) {
        return;
    }
    const attachments = [];
    for (const item of pending) {
        attachments.push({
            Id: CreateGUID(),
            FileName: item.file.name,
            ContentType: item.file.type || "application/octet-stream",
            Size: item.file.size,
            Data: new Uint8Array(await item.file.arrayBuffer()),
            FileId: null
        });
    }
    const dto = {
        Id: CreateGUID(),
        SenderName: ViewerApp.RequesterName || "",
        Text: text,
        Timestamp: new Date().toISOString(),
        Attachments: attachments
    };
    const element = RenderMessage(dto, true);
    (_a = list.querySelector(".chat-empty")) === null || _a === void 0 ? void 0 : _a.remove();
    list.appendChild(element);
    ownMessages.set(dto.Id, element);
    ScrollToEnd();
    input.value = "";
    AutoGrow();
    pending.forEach(x => x.url && URL.revokeObjectURL(x.url));
    pending = [];
    RenderPending();
    picker.classList.add("d-none");
    const status = element.querySelector(".chat-status");
    try {
        status === null || status === void 0 ? void 0 : status.classList.add("sending");
        await ViewerApp.ViewerHubConnection.SendDtoToClient(dto, DtoType.ChatMessage);
        status === null || status === void 0 ? void 0 : status.classList.remove("sending");
    }
    catch (e) {
        console.error(e);
        status === null || status === void 0 ? void 0 : status.classList.add("failed");
        status === null || status === void 0 ? void 0 : status.setAttribute("title", "Não enviada");
        ShowToast("Não foi possível enviar a mensagem.");
    }
}
function SendTyping() {
    const now = Date.now();
    if (now - lastTypingSent < 3000 || !input.value.trim()) {
        return;
    }
    lastTypingSent = now;
    const dto = { SenderName: ViewerApp.RequesterName || "", MessageId: null };
    ViewerApp.ViewerHubConnection.SendDtoToClient(dto, DtoType.ChatTyping);
}
function SendRead() {
    if (!lastIncomingId) {
        return;
    }
    const dto = { SenderName: ViewerApp.RequesterName || "", MessageId: lastIncomingId };
    lastIncomingId = null;
    ViewerApp.ViewerHubConnection.SendDtoToClient(dto, DtoType.ChatRead);
}
function RenderMessage(dto, mine) {
    const row = document.createElement("div");
    row.className = `chat-row ${mine ? "mine" : "theirs"}`;
    const bubble = document.createElement("div");
    bubble.className = "chat-bubble";
    if (!mine) {
        const sender = document.createElement("div");
        sender.className = "chat-sender";
        sender.textContent = dto.SenderName || "Usuário";
        bubble.appendChild(sender);
    }
    for (const attachment of dto.Attachments || []) {
        bubble.appendChild(RenderAttachment(attachment));
    }
    if (dto.Text) {
        const text = document.createElement("div");
        text.className = "chat-text";
        AppendLinkified(text, dto.Text);
        if (IsOnlyEmoji(dto.Text)) {
            text.classList.add("emoji-only");
        }
        bubble.appendChild(text);
    }
    const meta = document.createElement("div");
    meta.className = "chat-meta";
    const time = new Date(dto.Timestamp || Date.now());
    meta.textContent = time.toLocaleTimeString("pt-BR", { hour: "2-digit", minute: "2-digit" });
    if (mine) {
        const status = document.createElement("i");
        status.className = "fas fa-check-double chat-status";
        status.title = "Enviada";
        meta.appendChild(status);
    }
    bubble.appendChild(meta);
    row.appendChild(bubble);
    return row;
}
function RenderAttachment(attachment) {
    const data = attachment.Data;
    const blob = data ? new Blob([data], { type: attachment.ContentType || "application/octet-stream" }) : null;
    const url = blob ? URL.createObjectURL(blob) : null;
    const isImage = (attachment.ContentType || "").startsWith("image/") && !(attachment.ContentType || "").includes("svg");
    if (isImage && url) {
        const link = document.createElement("a");
        link.href = url;
        link.target = "_blank";
        link.rel = "noopener";
        link.className = "chat-image";
        link.title = `${attachment.FileName} · clique para abrir`;
        const img = document.createElement("img");
        img.src = url;
        img.alt = attachment.FileName;
        img.onload = () => ScrollToEnd();
        link.appendChild(img);
        return link;
    }
    const file = document.createElement("a");
    file.className = "chat-file";
    if (url) {
        file.href = url;
        file.download = attachment.FileName || "arquivo";
    }
    file.title = "Baixar";
    file.innerHTML = `<i class="fas ${FileIcon(attachment.FileName)}"></i>`;
    const info = document.createElement("span");
    info.className = "chat-file-info";
    const name = document.createElement("strong");
    name.textContent = attachment.FileName || "arquivo";
    const size = document.createElement("small");
    size.textContent = FormatSize(attachment.Size || (data === null || data === void 0 ? void 0 : data.length) || 0);
    info.append(name, size);
    file.appendChild(info);
    const download = document.createElement("i");
    download.className = "fas fa-download chat-file-download";
    file.appendChild(download);
    return file;
}
/** Texto com quebras de linha e links clicáveis, sem HTML vindo da mensagem. */
function AppendLinkified(target, text) {
    const pattern = /(https?:\/\/[^\s<]+)/g;
    let last = 0;
    let match;
    while ((match = pattern.exec(text)) != null) {
        target.appendChild(document.createTextNode(text.slice(last, match.index)));
        const a = document.createElement("a");
        a.href = match[0];
        a.target = "_blank";
        a.rel = "noopener noreferrer";
        a.textContent = match[0];
        target.appendChild(a);
        last = match.index + match[0].length;
    }
    target.appendChild(document.createTextNode(text.slice(last)));
}
/** Só emojis (sem letras nem números): aparecem maiores, como nos apps de mensagem. */
function IsOnlyEmoji(text) {
    const trimmed = text.replace(/\s/g, "");
    return trimmed.length > 0 && trimmed.length <= 16 &&
        !/[A-Za-z0-9À-ɏ.,;:!?()"'@#$%&*+=\/\\-]/.test(trimmed) &&
        /[\uD800-\uDBFF☀-➿]/.test(trimmed);
}
function BuildEmojiPicker() {
    picker.innerHTML = "";
    const tabs = document.createElement("div");
    tabs.className = "chat-emoji-tabs";
    const grid = document.createElement("div");
    grid.className = "chat-emoji-grid";
    const show = (index) => {
        grid.innerHTML = "";
        tabs.querySelectorAll("button").forEach((b, i) => b.classList.toggle("active", i == index));
        for (const emoji of EmojiGroups[index][2]) {
            const b = document.createElement("button");
            b.type = "button";
            b.textContent = emoji;
            b.onclick = ev => {
                ev.stopPropagation();
                InsertAtCaret(emoji);
            };
            grid.appendChild(b);
        }
    };
    EmojiGroups.forEach((group, index) => {
        const tab = document.createElement("button");
        tab.type = "button";
        tab.textContent = group[1];
        tab.title = group[0];
        tab.onclick = ev => {
            ev.stopPropagation();
            show(index);
        };
        tabs.appendChild(tab);
    });
    picker.append(tabs, grid);
    show(0);
}
function InsertAtCaret(value) {
    var _a, _b;
    const start = (_a = input.selectionStart) !== null && _a !== void 0 ? _a : input.value.length;
    const end = (_b = input.selectionEnd) !== null && _b !== void 0 ? _b : input.value.length;
    input.value = input.value.slice(0, start) + value + input.value.slice(end);
    const caret = start + value.length;
    input.setSelectionRange(caret, caret);
    input.focus();
    AutoGrow();
}
function AutoGrow() {
    input.style.height = "36px";
    if (input.scrollHeight > input.clientHeight) {
        input.style.height = `${Math.min(input.scrollHeight + 2, 140)}px`;
    }
}
function ScrollToEnd() {
    window.requestAnimationFrame(() => list.scrollTop = list.scrollHeight);
}
function UpdateBadge() {
    badge.textContent = unread > 9 ? "9+" : String(unread);
    badge.classList.toggle("d-none", unread == 0);
}
function FileIcon(name) {
    var _a;
    const ext = ((_a = (name || "").split(".").pop()) === null || _a === void 0 ? void 0 : _a.toLowerCase()) || "";
    if (["png", "jpg", "jpeg", "gif", "webp", "bmp"].includes(ext))
        return "fa-file-image";
    if (ext == "pdf")
        return "fa-file-pdf";
    if (["doc", "docx", "odt", "rtf"].includes(ext))
        return "fa-file-word";
    if (["xls", "xlsx", "csv", "ods"].includes(ext))
        return "fa-file-excel";
    if (["ppt", "pptx"].includes(ext))
        return "fa-file-powerpoint";
    if (["zip", "rar", "7z"].includes(ext))
        return "fa-file-zipper";
    if (["txt", "log", "md"].includes(ext))
        return "fa-file-lines";
    return "fa-file";
}
function FormatSize(bytes) {
    if (bytes >= 1024 * 1024)
        return `${(bytes / 1024 / 1024).toFixed(1).replace(".", ",")} MB`;
    if (bytes >= 1024)
        return `${Math.round(bytes / 1024)} KB`;
    return `${bytes} B`;
}
function PlayBeep() {
    try {
        const ctx = new AudioContext();
        const osc = ctx.createOscillator();
        const gain = ctx.createGain();
        osc.frequency.value = 880;
        gain.gain.setValueAtTime(0.06, ctx.currentTime);
        gain.gain.exponentialRampToValueAtTime(0.0001, ctx.currentTime + 0.25);
        osc.connect(gain).connect(ctx.destination);
        osc.start();
        osc.stop(ctx.currentTime + 0.26);
        osc.onended = () => ctx.close();
    }
    catch (_a) { }
}
//# sourceMappingURL=ChatPanel.js.map