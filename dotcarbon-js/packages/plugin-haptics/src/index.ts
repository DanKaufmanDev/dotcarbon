import { invoke } from '@dotcarbon/api'

export type ImpactStyle = 'light' | 'medium' | 'heavy'
export type NotificationType = 'success' | 'warning' | 'error'

/** Play impact feedback. No-op on desktop. */
export const impact = (style: ImpactStyle = 'medium'): Promise<void> =>
    invoke('haptics:impact', { style })

/** Play notification feedback. No-op on desktop. */
export const notification = (type: NotificationType = 'success'): Promise<void> =>
    invoke('haptics:notification', { type })

/** Vibrate for a duration in milliseconds (clamped to 5000). No-op on desktop. */
export const vibrate = (durationMs = 100): Promise<void> =>
    invoke('haptics:vibrate', { durationMs })

declare module '@dotcarbon/api' {
    interface CarbonCommands {
        'haptics:impact': { args: { style: ImpactStyle }; result: void }
        'haptics:notification': { args: { type: NotificationType }; result: void }
        'haptics:vibrate': { args: { durationMs: number }; result: void }
    }
}
