import { ScreenViewer } from "./UI.js";
import { ViewerApp } from "./App.js";

export class SessionRecorder {
    private recorder: MediaRecorder;
    private subject: any;
    private finished: Promise<void> = Promise.resolve();

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
        let pending: Promise<void> = Promise.resolve();

        this.recorder.ondataavailable = (event) => {
            if (!event.data || event.data.size <= 0) {
                return;
            }
            pending = pending.then(async () => {
                const buffer = await event.data.arrayBuffer();
                for (let i = 0; i < buffer.byteLength; i += 50_000) {
                    subject.next(new Uint8Array(buffer.slice(i, i + 50_000)));
                }
            }).catch(e => console.warn("Recording chunk failed.", e));
        };
        let done: () => void;
        this.finished = new Promise<void>(resolve => done = resolve);
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
        if (this.recorder && this.recorder.state != "inactive") {
            this.recorder.stop();
        }
        else {
            this.subject?.complete();
        }
        this.subject = null;
    }

}