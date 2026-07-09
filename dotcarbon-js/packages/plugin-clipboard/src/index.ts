import { invoke } from '@dotcarbon/api'

export const clipboard = {
    readText: (): Promise<string> =>
        invoke('clipboard:read_text'),

    writeText: (text: string): Promise<void> =>
        invoke('clipboard:write_text', { text }),

    clear: (): Promise<void> =>
        invoke('clipboard:clear'),
}

declare module '@dotcarbon/api' {
    interface CarbonCommands {
        'clipboard:read_text': { args: void; result: string }
        'clipboard:write_text': { args: { text: string }; result: void }
        'clipboard:clear': { args: void; result: void }
    }
}
