import { invoke } from '@dotcarbon/api'

export type Scope = 'fs' | 'asset'

/** Grant a path to a scope and remember it across restarts. */
export const allow = (scope: Scope, path: string): Promise<void> =>
    invoke('persisted-scope:allow', { scope, path })

/** The persisted grants, keyed by scope. */
export const list = (): Promise<Record<string, string[]>> =>
    invoke('persisted-scope:list')

/** Forget all persisted grants. */
export const clear = (): Promise<void> =>
    invoke('persisted-scope:clear')

declare module '@dotcarbon/api' {
    interface CarbonCommands {
        'persisted-scope:allow': { args: { scope: string; path: string }; result: void }
        'persisted-scope:list': { args: void; result: Record<string, string[]> }
        'persisted-scope:clear': { args: void; result: void }
    }
}
