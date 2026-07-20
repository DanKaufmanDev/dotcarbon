import { invoke } from '@dotcarbon/api'

export interface WindowState {
    width: number
    height: number
    x: number
    y: number
    maximized: boolean
}

/** Capture every window's geometry and write it now (also happens automatically on close/exit). */
export const saveWindowState = (): Promise<void> => invoke('window-state:save')

/** Re-apply the saved geometry to a window. */
export const restoreState = (label: string): Promise<void> => invoke('window-state:restore', { label })

/** The saved state for a window, or null if none has been stored. */
export const getState = (label: string): Promise<WindowState | null> => invoke('window-state:get', { label })

/** Forget all saved state and delete the state file. */
export const clearState = (): Promise<void> => invoke('window-state:clear')

declare module '@dotcarbon/api' {
    interface CarbonCommands {
        'window-state:save': { args: void; result: void }
        'window-state:restore': { args: { label: string }; result: void }
        'window-state:get': { args: { label: string }; result: WindowState | null }
        'window-state:clear': { args: void; result: void }
    }
}
