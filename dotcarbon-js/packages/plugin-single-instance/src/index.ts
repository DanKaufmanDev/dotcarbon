import { invoke } from '@dotcarbon/api'

export interface SingleInstanceStatus {
    isPrimary: boolean
    mutexName: string
    args: string[]
}

export const singleInstance = {
    status: (): Promise<SingleInstanceStatus> =>
        invoke('single-instance:status'),

    isPrimary: (): Promise<boolean> =>
        invoke('single-instance:is_primary'),
}

declare module '@dotcarbon/api' {
    interface CarbonCommands {
        'single-instance:status': { args: void; result: SingleInstanceStatus }
        'single-instance:is_primary': { args: void; result: boolean }
    }
}
