import { invoke, listen } from '@dotcarbon/api'

export interface RegisterShortcutOptions {
    id: string
    accelerator: string
    suppress?: boolean
}

export interface ShortcutInfo {
    id: string
    accelerator: string
    suppress: boolean
}

export interface GlobalShortcutPressed {
    id: string
    accelerator: string
}

export const globalShortcut = {
    register: (options: RegisterShortcutOptions): Promise<ShortcutInfo> =>
        invoke('global-shortcut:register', {
            id: options.id,
            accelerator: options.accelerator,
            suppress: options.suppress ?? false,
        }),

    unregister: (id: string): Promise<boolean> =>
        invoke('global-shortcut:unregister', { id }),

    unregisterAll: (): Promise<boolean> =>
        invoke('global-shortcut:unregister_all'),

    list: (): Promise<ShortcutInfo[]> =>
        invoke('global-shortcut:list'),

    onPressed: (handler: (event: GlobalShortcutPressed) => void): Promise<() => void> =>
        listen('global-shortcut:pressed', event => handler(event.payload)),
}

declare module '@dotcarbon/api' {
    interface CarbonCommands {
        'global-shortcut:register': { args: { id: string; accelerator: string; suppress: boolean }; result: ShortcutInfo }
        'global-shortcut:unregister': { args: { id: string }; result: boolean }
        'global-shortcut:unregister_all': { args: void; result: boolean }
        'global-shortcut:list': { args: void; result: ShortcutInfo[] }
    }

    interface CarbonEvents {
        'global-shortcut:pressed': GlobalShortcutPressed
    }
}
