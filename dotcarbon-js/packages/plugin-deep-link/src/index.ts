import { invoke, listen } from '@dotcarbon/api'

export interface DeepLinkInfo {
    schemes: string[]
    pending: string[]
}

export const deepLink = {
    getPending: (): Promise<string[]> =>
        invoke('deep-link:get_pending'),

    schemes: (): Promise<string[]> =>
        invoke('deep-link:schemes'),

    info: (): Promise<DeepLinkInfo> =>
        invoke('deep-link:info'),

    onOpen: (handler: (url: string) => void): Promise<() => void> =>
        listen('deep-link:opened', event => handler(event.payload)),
}

declare module '@dotcarbon/api' {
    interface CarbonCommands {
        'deep-link:get_pending': { args: void; result: string[] }
        'deep-link:schemes': { args: void; result: string[] }
        'deep-link:info': { args: void; result: DeepLinkInfo }
    }

    interface CarbonEvents {
        'deep-link:opened': string
    }
}
