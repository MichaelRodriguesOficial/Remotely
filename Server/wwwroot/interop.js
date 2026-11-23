window.addClassName = (element, className) => {
    element.classList.add(className);
}

window.getSelectionStart = (element) => {
    return element.selectionStart;
}

window.invokeClick = elementId => {
    document.getElementById(elementId).click();
}

window.invokeConfirm = async (message) => {
    return confirm(message);
}

window.invokeAlert = async (message) => {
    alert(message);
}

window.invokePrompt = async (message) => {
    return prompt(message);
}

window.preventTabOut = (element) => {
    element.addEventListener("keydown", ev => {
        if (ev.key.toLowerCase() == "tab") {
            ev.preventDefault();
        }
    })
}
window.scrollToEnd = (element) => {
    if (!element) {
        return;
    }

    element.scrollTop = element.scrollHeight;
}
window.scrollToElement = (element) => {
    window.setTimeout(() => {
        window.scrollTo({ top: element.offsetTop, behavior: "smooth"});
    }, 200);

}

/**
 * @param {string} text
 * @returns {Promise<boolean>}
 */
window.setClipboardText = async (text) => {
    try {
        await navigator.clipboard.writeText(text);
        return true;
    }
    catch (ex) {
        console.error(ex);
        return false;
    }
}

window.setStyleProperty = (element, propertyName, value) => {
    element.style[propertyName] = value;
}

window.startDraggingY = (element, clientY) => {
    if (!element) {
        return;
    }

    var startTop = Number(clientY);

    function pointerMove(ev) {
        if (Math.abs(ev.clientY - startTop) > 10) {
            if (ev.clientY < 0 || ev.clientY > window.innerHeight - element.clientHeight) {
                return;
            }

            element.style.top = `${ev.clientY}px`;
        }
    }

    function pointerUpOrLeave(ev) {
        window.removeEventListener("pointermove", pointerMove);
        window.removeEventListener("pointerup", pointerUpOrLeave);
        window.removeEventListener("pointerleave", pointerUpOrLeave);
    }

    pointerUpOrLeave();

    window.addEventListener("pointermove", pointerMove);
    window.addEventListener("pointerup", pointerUpOrLeave);
    window.addEventListener("pointerleave", pointerUpOrLeave);
}

window.autoHeight = () => {
    document.querySelectorAll(".auto-height").forEach(x => {
        var desiredHeight = window.innerHeight - x.getBoundingClientRect().top;
        x.style.height = `${desiredHeight}px`;
        x.style.opacity = 1;
    })
}

window.openWindow = (url, target) => {
    window.open(url, target);
}

window.addEventListener("load", () => {
    window.addEventListener("resize", () => {
        autoHeight();
    });
    autoHeight();
})

window.addBeforeUnloadHandler = () => {
    window.addEventListener("beforeunload", (ev) => {
        if (location.href.indexOf("localhost") == -1) {
            // All three methods have been implemented in different browsers at some point.
            // Might as well cover all bases.
            ev.preventDefault();
            var msg = "This will disconnect your session.  Are you sure you want to exit?";
            ev.returnValue = msg;
            return msg;
        }
    })
}
// Baixa um arquivo gerado no servidor (exportações).
window.remotelyDownloadFile = (fileName, contentType, base64) => {
    const bytes = Uint8Array.from(atob(base64), c => c.charCodeAt(0));
    const url = URL.createObjectURL(new Blob([bytes], { type: contentType }));
    const link = document.createElement("a");
    link.href = url;
    link.download = fileName;
    document.body.appendChild(link);
    link.click();
    link.remove();
    setTimeout(() => URL.revokeObjectURL(url), 10000);
};

// Abre a impressão de um relatório HTML (o navegador oferece "Salvar como PDF").
// O relatório fica num iframe isolado, sem scripts.
window.remotelyPrintHtml = (html) => {
    const frame = document.createElement("iframe");
    frame.setAttribute("sandbox", "allow-modals allow-same-origin");
    frame.style.position = "fixed";
    frame.style.right = "0";
    frame.style.bottom = "0";
    frame.style.width = "0";
    frame.style.height = "0";
    frame.style.border = "0";
    frame.onload = () => {
        setTimeout(() => {
            frame.contentWindow.focus();
            frame.contentWindow.print();
            setTimeout(() => frame.remove(), 60000);
        }, 200);
    };
    frame.srcdoc = html;
    document.body.appendChild(frame);
};

window.remotelyScrollIntoView = (id) => {
    const el = document.getElementById(id);
    if (el) {
        el.scrollIntoView({ behavior: "smooth", block: "start" });
    }
};
