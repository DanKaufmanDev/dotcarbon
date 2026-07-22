import { invoke } from '@dotcarbon/api'

export interface GeolocationPosition {
    latitude: number
    longitude: number
    /** Horizontal accuracy in metres, or null when unreported. */
    accuracy: number | null
    altitude: number | null
    speed: number | null
    /** Unix milliseconds. */
    timestamp: number
}

/**
 * The current position, or null if no fix arrives within `timeoutMs`.
 * Request the 'location' permission first (@dotcarbon/plugin-permissions).
 */
export const current = (timeoutMs = 10_000): Promise<GeolocationPosition | null> =>
    invoke('geolocation:current', { timeoutMs })

declare module '@dotcarbon/api' {
    interface CarbonCommands {
        'geolocation:current': { args: { timeoutMs: number }; result: GeolocationPosition | null }
    }
}
