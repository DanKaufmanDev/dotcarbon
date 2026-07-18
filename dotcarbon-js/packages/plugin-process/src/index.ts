import { invoke } from '@dotcarbon/api'

/** The current process id. */
export const pid = (): Promise<number> => invoke('process:pid')

/** Exit the app immediately with the given code (default 0). */
export const exit = (code = 0): Promise<void> => invoke('process:exit', { code })

/** Start a fresh copy of the app with the same arguments, then exit this one. */
export const relaunch = (): Promise<void> => invoke('process:relaunch')

declare module '@dotcarbon/api' {
    interface CarbonCommands {
        'process:pid': { args: void; result: number }
        'process:exit': { args: { code: number }; result: void }
        'process:relaunch': { args: void; result: void }
    }
}
