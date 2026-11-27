import { ScreenViewer } from "./UI.js";
import { ViewerApp } from "./App.js";
export class SessionRecorder {
    constructor() {
        this.finished = Promise.resolve();
    }
    async Start() {
        if (!window["MediaRecorder"] || !ScreenViewer.captureStream) {
            console.warn("Session recording isn't supported on this browser.");
            return;
        }
        if (this.recorder && this.recorder.state != "inactive") {
            this.recorder.stop();
        }
        const stream = ScreenViewer.captureStream(10);
        const options = { mimeType: 'video/webm' };
        this.subject = new window["signalR"].Subject();
        await ViewerApp.ViewerHubConnection.SendRecordingChunks(this.subject);
        this.recorder = new MediaRecorder(stream, options);
        const subject = this.subject;
        // Os pedaços vão em ordem; o último (gerado no stop) também entra antes de fechar o envio.
        let pending = Promise.resolve();
        this.recorder.ondataavailable = (event) => {
            if (!event.data || event.data.size <= 0) {
                return;
            }
            pending = pending.then(async () => {
                const buffer = await event.data.arrayBuffer();
                for (let i = 0; i < buffer.byteLength; i += 50000) {
                    subject.next(new Uint8Array(buffer.slice(i, i + 50000)));
                }
            }).catch(e => console.warn("Recording chunk failed.", e));
        };
        let done;
        this.finished = new Promise(resolve => done = resolve);
        this.recorder.onstop = () => {
            pending.then(() => subject.complete()).then(() => done(), () => done());
        };
        this.recorder.start(250);
    }
    /** Encerrar pelo técnico: manda o último pedaço antes de a conexão fechar (no máximo 2 s). */
    async StopAndFlush() {
        if (!this.recorder || this.recorder.state == "inactive") {
            return;
        }
        const finished = this.finished;
        this.Stop();
        await Promise.race([finished, new Promise(resolve => window.setTimeout(resolve, 2000))]);
    }
    Stop() {
        var _a;
        if (this.recorder && this.recorder.state != "inactive") {
            this.recorder.stop();
        }
        else {
            (_a = this.subject) === null || _a === void 0 ? void 0 : _a.complete();
        }
        this.subject = null;
    }
}
//# sourceMappingURL=SessionRecorder.js.map