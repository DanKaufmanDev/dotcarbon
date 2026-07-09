import { invoke } from '@dotcarbon/api'

export interface NotificationOptions {
    title: string
    body: string
    subtitle?: string
}

export const notification = {
    send: (options: NotificationOptions): Promise<void> =>
        invoke('notification:send', {
            title: options.title,
            body: options.body,
            subtitle: options.subtitle ?? null,
        }),
}