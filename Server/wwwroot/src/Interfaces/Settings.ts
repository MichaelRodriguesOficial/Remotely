import { RemoteControlViewerOptions } from "./Dtos.js";

export interface Settings {
    DisplayName: string;
    ViewerOptions: RemoteControlViewerOptions;
    /** Copiar e colar automático entre o navegador e o computador remoto (padrão: ligado). */
    ClipboardSync?: boolean;
    /** Barra de ferramentas recolhida. */
    ToolbarCollapsed?: boolean;
}