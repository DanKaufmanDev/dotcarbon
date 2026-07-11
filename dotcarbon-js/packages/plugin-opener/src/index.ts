import { invoke } from '@dotcarbon/api'

export const opener = {
    openPath: (path: string): Promise<boolean> =>
        invoke('opener:open_path', { path }),

    openUrl: (url: string): Promise<boolean> =>
        invoke('opener:open_url', { url }),

    revealPath: (path: string): Promise<boolean> =>
        invoke('opener:reveal_path', { path }),
}

declare module '@dotcarbon/api' {
    interface CarbonCommands {
        'opener:open_path': { args: { path: string }; result: boolean }
        'opener:open_url': { args: { url: string }; result: boolean }
        'opener:reveal_path': { args: { path: string }; result: boolean }
    }
}
