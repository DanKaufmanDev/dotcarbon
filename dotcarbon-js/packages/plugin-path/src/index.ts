import { invoke } from '@dotcarbon/api'

// --- app directories ---------------------------------------------------------------------------
// Each resolves to an OS-standard location under the app's identifier from carbon.json.

export const homeDir = (): Promise<string> => invoke('path:home_dir')
export const tempDir = (): Promise<string> => invoke('path:temp_dir')
/** The directory holding the app's bundled resources (bundle.resources). */
export const resourceDir = (): Promise<string> => invoke('path:resource_dir')
/** Resolve a path relative to the resource directory, e.g. resolveResource('assets/icon.png'). */
export const resolveResource = (path: string): Promise<string> => invoke('path:resolve_resource', { path })
export const appConfigDir = (): Promise<string> => invoke('path:app_config_dir')
export const appDataDir = (): Promise<string> => invoke('path:app_data_dir')
export const appCacheDir = (): Promise<string> => invoke('path:app_cache_dir')
export const appLogDir = (): Promise<string> => invoke('path:app_log_dir')

// --- path manipulation -------------------------------------------------------------------------
// These run on the backend so they use the running OS's separator.

/** Combine segments and resolve to an absolute path. */
export const resolve = (...parts: string[]): Promise<string> => invoke('path:resolve', { parts })
/** Join segments with the OS separator (no resolution). */
export const join = (...parts: string[]): Promise<string> => invoke('path:join', { parts })
export const dirname = (path: string): Promise<string> => invoke('path:dirname', { path })
export const basename = (path: string): Promise<string> => invoke('path:basename', { path })
export const extname = (path: string): Promise<string> => invoke('path:extname', { path })
export const normalize = (path: string): Promise<string> => invoke('path:normalize', { path })
export const isAbsolute = (path: string): Promise<boolean> => invoke('path:is_absolute', { path })
/** The OS path separator ("/" or "\"). */
export const sep = (): Promise<string> => invoke('path:sep')
/** The OS path-list delimiter (":" or ";"). */
export const delimiter = (): Promise<string> => invoke('path:delimiter')

declare module '@dotcarbon/api' {
    interface CarbonCommands {
        'path:home_dir': { args: void; result: string }
        'path:temp_dir': { args: void; result: string }
        'path:resource_dir': { args: void; result: string }
        'path:resolve_resource': { args: { path: string }; result: string }
        'path:app_config_dir': { args: void; result: string }
        'path:app_data_dir': { args: void; result: string }
        'path:app_cache_dir': { args: void; result: string }
        'path:app_log_dir': { args: void; result: string }
        'path:resolve': { args: { parts: string[] }; result: string }
        'path:join': { args: { parts: string[] }; result: string }
        'path:dirname': { args: { path: string }; result: string }
        'path:basename': { args: { path: string }; result: string }
        'path:extname': { args: { path: string }; result: string }
        'path:normalize': { args: { path: string }; result: string }
        'path:is_absolute': { args: { path: string }; result: boolean }
        'path:sep': { args: void; result: string }
        'path:delimiter': { args: void; result: string }
    }
}
