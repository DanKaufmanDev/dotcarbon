import { invoke } from '@dotcarbon/api'

/** Store a secret under a key in the OS credential store. */
export const set = (key: string, value: string): Promise<void> =>
    invoke('secure-storage:set', { key, value })

/** Read a secret, or null if it isn't set. */
export const get = (key: string): Promise<string | null> =>
    invoke('secure-storage:get', { key })

/** Delete a secret. */
export const remove = (key: string): Promise<void> =>
    invoke('secure-storage:remove', { key })

/** Whether a secret is set. */
export const has = (key: string): Promise<boolean> =>
    invoke('secure-storage:has', { key })

declare module '@dotcarbon/api' {
    interface CarbonCommands {
        'secure-storage:set': { args: { key: string; value: string }; result: void }
        'secure-storage:get': { args: { key: string }; result: string | null }
        'secure-storage:remove': { args: { key: string }; result: void }
        'secure-storage:has': { args: { key: string }; result: boolean }
    }
}
