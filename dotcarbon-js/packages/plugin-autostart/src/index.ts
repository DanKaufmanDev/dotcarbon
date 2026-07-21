import { invoke } from '@dotcarbon/api'

/** Register the app to launch automatically at login. */
export const enable = (): Promise<void> => invoke('autostart:enable')

/** Stop launching the app at login. */
export const disable = (): Promise<void> => invoke('autostart:disable')

/** Whether the app is currently set to launch at login. */
export const isEnabled = (): Promise<boolean> => invoke('autostart:is_enabled')

declare module '@dotcarbon/api' {
    interface CarbonCommands {
        'autostart:enable': { args: void; result: void }
        'autostart:disable': { args: void; result: void }
        'autostart:is_enabled': { args: void; result: boolean }
    }
}
