import { invoke } from '@dotcarbon/api'

export interface StoreEntry<T = unknown> {
    key: string
    value: T
}

export interface StoreSnapshot<T = unknown> {
    store: string
    entries: StoreEntry<T>[]
}

export interface StoreOptions {
    store?: string
}

export const store = {
    get: <T = unknown>(key: string, options: StoreOptions = {}): Promise<T | null> =>
        invoke('store:get', { key, store: options.store ?? null }) as Promise<T | null>,

    set: <T = unknown>(key: string, value: T, options: StoreOptions = {}): Promise<StoreSnapshot<T>> =>
        invoke('store:set', { key, value, store: options.store ?? null }) as Promise<StoreSnapshot<T>>,

    delete: (key: string, options: StoreOptions = {}): Promise<StoreSnapshot> =>
        invoke('store:delete', { key, store: options.store ?? null }),

    clear: (options: StoreOptions = {}): Promise<StoreSnapshot> =>
        invoke('store:clear', { store: options.store ?? null }),

    entries: <T = unknown>(options: StoreOptions = {}): Promise<StoreSnapshot<T>> =>
        invoke('store:entries', { store: options.store ?? null }) as Promise<StoreSnapshot<T>>,

    keys: (options: StoreOptions = {}): Promise<string[]> =>
        invoke('store:keys', { store: options.store ?? null }),
}

declare module '@dotcarbon/api' {
    interface CarbonCommands {
        'store:get': { args: { key: string; store: string | null }; result: unknown | null }
        'store:set': { args: { key: string; value: unknown; store: string | null }; result: StoreSnapshot }
        'store:delete': { args: { key: string; store: string | null }; result: StoreSnapshot }
        'store:clear': { args: { store: string | null }; result: StoreSnapshot }
        'store:entries': { args: { store: string | null }; result: StoreSnapshot }
        'store:keys': { args: { store: string | null }; result: string[] }
    }
}
