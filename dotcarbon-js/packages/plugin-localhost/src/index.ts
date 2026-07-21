import { invoke } from '@dotcarbon/api'

/** The URL the app is being served from (e.g. http://127.0.0.1:8123), or "" if not running. */
export const url = (): Promise<string> => invoke('localhost:url')

/** (Re)start the server on a port (0 picks a free one); returns the URL. */
export const start = (port = 0): Promise<string> => invoke('localhost:start', { port })

/** Stop the server. */
export const stop = (): Promise<void> => invoke('localhost:stop')

declare module '@dotcarbon/api' {
    interface CarbonCommands {
        'localhost:url': { args: void; result: string }
        'localhost:start': { args: { port: number }; result: string }
        'localhost:stop': { args: void; result: void }
    }
}
