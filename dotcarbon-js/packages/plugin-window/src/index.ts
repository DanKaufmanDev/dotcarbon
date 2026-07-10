import { invoke } from '@dotcarbon/api'

export interface WindowState {
    label: string
    title: string
    width: number
    height: number
    x: number
    y: number
    fullscreen: boolean
    maximized: boolean
    minimized: boolean
    alwaysOnTop: boolean
    resizable: boolean
}

export interface CreateWindowOptions {
    label: string
    url?: string
    parentLabel?: string
    title?: string
    width?: number
    height?: number
    minWidth?: number
    minHeight?: number
    maxWidth?: number
    maxHeight?: number
    x?: number
    y?: number
    center?: boolean
    resizable?: boolean
    fullscreen?: boolean
    maximized?: boolean
    alwaysOnTop?: boolean
    decorations?: boolean
    transparent?: boolean
    devTools?: boolean
    contextMenu?: boolean
    icon?: string
}

export class WebviewWindow {
    constructor(public readonly label: string) {}

    static async create(options: CreateWindowOptions): Promise<WebviewWindow> {
        const state = await invoke('window:create', options)
        return new WebviewWindow(state.label)
    }

    static async getCurrent(): Promise<WebviewWindow> {
        const state = await invoke('window:get_state', {})
        return new WebviewWindow(state.label)
    }

    static async getByLabel(label: string): Promise<WebviewWindow | null> {
        const state = await invoke('window:get_by_label', { label })
        return state ? new WebviewWindow(state.label) : null
    }

    static async getAll(): Promise<WebviewWindow[]> {
        const states = await invoke('window:get_all')
        return states.map(state => new WebviewWindow(state.label))
    }

    getState = (): Promise<WindowState> =>
        invoke('window:get_state', { label: this.label })

    setTitle = (title: string): Promise<void> =>
        invoke('window:set_title', { title, label: this.label })

    setSize = (width: number, height: number): Promise<void> =>
        invoke('window:set_size', { width, height, label: this.label })

    setPosition = (x: number, y: number): Promise<void> =>
        invoke('window:set_position', { x, y, label: this.label })

    center = (): Promise<void> =>
        invoke('window:center', { label: this.label })

    minimize = (): Promise<void> =>
        invoke('window:minimize', { label: this.label })

    maximize = (): Promise<void> =>
        invoke('window:maximize', { label: this.label })

    unmaximize = (): Promise<void> =>
        invoke('window:unmaximize', { label: this.label })

    setFullscreen = (fullscreen: boolean): Promise<void> =>
        invoke('window:set_fullscreen', { fullscreen, label: this.label })

    setAlwaysOnTop = (alwaysOnTop: boolean): Promise<void> =>
        invoke('window:set_always_on_top', { alwaysOnTop, label: this.label })

    setResizable = (resizable: boolean): Promise<void> =>
        invoke('window:set_resizable', { resizable, label: this.label })

    close = (): Promise<void> =>
        invoke('window:close', { label: this.label })
}

export { WebviewWindow as CarbonWindow }

export const getCurrentWindow = WebviewWindow.getCurrent
export const getAllWindows = WebviewWindow.getAll
export const getWindowByLabel = WebviewWindow.getByLabel
export const createWindow = WebviewWindow.create

export const carbonWindow = {
    getState: (): Promise<WindowState> => invoke('window:get_state', {}),
    setTitle: (title: string): Promise<void> => invoke('window:set_title', { title }),
    setSize: (width: number, height: number): Promise<void> =>
        invoke('window:set_size', { width, height }),
    setPosition: (x: number, y: number): Promise<void> =>
        invoke('window:set_position', { x, y }),
    center: (): Promise<void> => invoke('window:center', {}),
    minimize: (): Promise<void> => invoke('window:minimize', {}),
    maximize: (): Promise<void> => invoke('window:maximize', {}),
    unmaximize: (): Promise<void> => invoke('window:unmaximize', {}),
    setFullscreen: (fullscreen: boolean): Promise<void> =>
        invoke('window:set_fullscreen', { fullscreen }),
    setAlwaysOnTop: (alwaysOnTop: boolean): Promise<void> =>
        invoke('window:set_always_on_top', { alwaysOnTop }),
    setResizable: (resizable: boolean): Promise<void> =>
        invoke('window:set_resizable', { resizable }),
    close: (): Promise<void> => invoke('window:close', {}),
}

declare module '@dotcarbon/api' {
    interface CarbonCommands {
        'window:create': { args: CreateWindowOptions; result: WindowState }
        'window:get_all': { args: void; result: WindowState[] }
        'window:get_by_label': { args: { label: string }; result: WindowState | null }
        'window:get_state': { args: { label?: string }; result: WindowState }
        'window:set_title': { args: { title: string; label?: string }; result: void }
        'window:set_size': { args: { width: number; height: number; label?: string }; result: void }
        'window:set_position': { args: { x: number; y: number; label?: string }; result: void }
        'window:center': { args: { label?: string }; result: void }
        'window:minimize': { args: { label?: string }; result: void }
        'window:maximize': { args: { label?: string }; result: void }
        'window:unmaximize': { args: { label?: string }; result: void }
        'window:set_fullscreen': { args: { fullscreen: boolean; label?: string }; result: void }
        'window:set_always_on_top': { args: { alwaysOnTop: boolean; label?: string }; result: void }
        'window:set_resizable': { args: { resizable: boolean; label?: string }; result: void }
        'window:close': { args: { label?: string }; result: void }
    }
}
