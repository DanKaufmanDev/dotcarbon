import { invoke } from '@dotcarbon/api'

export type Position =
    | 'TopLeft'
    | 'TopRight'
    | 'BottomLeft'
    | 'BottomRight'
    | 'TopCenter'
    | 'BottomCenter'
    | 'LeftCenter'
    | 'RightCenter'
    | 'Center'

/** Move a window to a named position on its monitor's work area. Omit `label` for the current window. */
export const moveWindow = (position: Position, label?: string): Promise<void> =>
    invoke('positioner:move', { position, label: label ?? null })

declare module '@dotcarbon/api' {
    interface CarbonCommands {
        'positioner:move': { args: { position: string; label: string | null }; result: void }
    }
}
