import { invoke } from '@dotcarbon/api'

export interface FetchOptions {
    method?: string
    headers?: Record<string, string>
    body?: string
}

export interface FetchResult {
    status: number
    statusText: string
    headers: Record<string, string>
    body: string
}

export const http = {
    fetch: (url: string, options: FetchOptions = {}): Promise<FetchResult> =>
        invoke('http:fetch', {
            url,
            method: options.method,
            headers: options.headers,
            body: options.body,
        }),
}

declare module '@dotcarbon/api' {
    interface CarbonCommands {
        'http:fetch': {
            args: { url: string; method?: string; headers?: Record<string, string>; body?: string }
            result: { status: number; statusText: string; headers: Record<string, string>; body: string }
        }
    }
}
