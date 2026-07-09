import { invoke } from '@dotcarbon/api'

export interface WindowState {
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

export const carbonWindow = {
    getState: (): Promise<WindowState> =>
        invoke('window:get_state'),

    setTitle: (title: string): Promise<void> =>
        invoke('window:set_title', { title }),

    setSize: (width: number, height: number): Promise<void> =>
        invoke('window:set_size', { width, height }),

    setPosition: (x: number, y: number): Promise<void> =>
        invoke('window:set_position', { x, y }),

    center: (): Promise<void> =>
        invoke('window:center'),

    minimize: (): Promise<void> =>
        invoke('window:minimize'),

    maximize: (): Promise<void> =>
        invoke('window:maximize'),

    unmaximize: (): Promise<void> =>
        invoke('window:unmaximize'),

    setFullscreen: (fullscreen: boolean): Promise<void> =>
        invoke('window:set_fullscreen', { fullscreen }),

    setAlwaysOnTop: (alwaysOnTop: boolean): Promise<void> =>
        invoke('window:set_always_on_top', { alwaysOnTop }),

    setResizable: (resizable: boolean): Promise<void> =>
        invoke('window:set_resizable', { resizable }),

    close: (): Promise<void> =>
        invoke('window:close'),
}