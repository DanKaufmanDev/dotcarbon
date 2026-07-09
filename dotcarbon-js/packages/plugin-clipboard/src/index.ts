import { invoke } from '@dotcarbon/api'

export const clipboard = {
    readText: (): Promise<string> =>
        invoke('clipboard:read_text'),

    writeText: (text: string): Promise<void> =>
        invoke('clipboard:write_text', { text }),

    clear: (): Promise<void> =>
        invoke('clipboard:clear'),
}