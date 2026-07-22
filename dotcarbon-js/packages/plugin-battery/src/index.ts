import { invoke } from '@dotcarbon/api'

export interface BatteryStatus {
    /** Charge level from 0 to 1, or null when unknown (e.g. a desktop with no battery). */
    level: number | null
    /** Whether the battery is charging, or null when unknown. */
    charging: boolean | null
    /** One of 'charging' | 'discharging' | 'full' | 'unknown'. */
    state: string
}

/** Read the current device battery status. */
export const status = (): Promise<BatteryStatus> => invoke('battery:status')

declare module '@dotcarbon/api' {
    interface CarbonCommands {
        'battery:status': { args: void; result: BatteryStatus }
    }
}
