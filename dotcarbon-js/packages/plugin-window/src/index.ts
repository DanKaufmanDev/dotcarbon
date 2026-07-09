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

declare module '@dotcarbon/api' {
    interface CarbonCommands {
        'window:get_state': { args: void; result: WindowState }
        'window:set_title': { args: { title: string }; result: void }
        'window:set_size': { args: { width: number; height: number }; result: void }
        'window:set_position': { args: { x: number; y: number }; result: void }
        'window:center': { args: void; result: void }
        'window:minimize': { args: void; result: void }
        'window:maximize': { args: void; result: void }
        'window:unmaximize': { args: void; result: void }
        'window:set_fullscreen': { args: { fullscreen: boolean }; result: void }
        'window:set_always_on_top': { args: { alwaysOnTop: boolean }; result: void }
        'window:set_resizable': { args: { resizable: boolean }; result: void }
        'window:close': { args: void; result: void }
    }
}
