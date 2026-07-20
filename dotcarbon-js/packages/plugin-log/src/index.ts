import { invoke, listen, type UnlistenFn } from '@dotcarbon/api'

export type LogLevel = 'trace' | 'debug' | 'info' | 'warn' | 'error'

export interface LogRecord {
    level: LogLevel
    message: string
    timestamp: string
    location: string | null
}

const write = (level: LogLevel, message: string, location?: string): Promise<void> =>
    invoke('log:log', { level, message, location: location ?? null })

export const trace = (message: string, location?: string): Promise<void> => write('trace', message, location)
export const debug = (message: string, location?: string): Promise<void> => write('debug', message, location)
export const info = (message: string, location?: string): Promise<void> => write('info', message, location)
export const warn = (message: string, location?: string): Promise<void> => write('warn', message, location)
export const error = (message: string, location?: string): Promise<void> => write('error', message, location)

/**
 * Print backend log records in the webview console (the "webview" target). Returns a detach function.
 * Mirrors Tauri's log plugin attachConsole().
 */
export async function attachConsole(): Promise<UnlistenFn> {
    return listen('log:message', ({ payload }) => {
        const line = `[${payload.level.toUpperCase()}] ${payload.message}`
        if (payload.level === 'error') console.error(line)
        else if (payload.level === 'warn') console.warn(line)
        else if (payload.level === 'debug' || payload.level === 'trace') console.debug(line)
        else console.info(line)
    })
}

declare module '@dotcarbon/api' {
    interface CarbonCommands {
        'log:log': { args: { level: string; message: string; location: string | null }; result: void }
    }
    interface CarbonEvents {
        'log:message': LogRecord
    }
}
