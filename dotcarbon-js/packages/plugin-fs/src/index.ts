import { invoke } from '@dotcarbon/api'

export interface DirEntry {
    name: string
    path: string
    isDirectory: boolean
    size: number
    lastModified: string
}

export const fs = {
    readFile: (path: string): Promise<string> =>
        invoke('fs:read_file', { path }),

    writeFile: (path: string, contents: string): Promise<void> =>
        invoke('fs:write_file', { path, contents }),

    readDir: (path: string): Promise<DirEntry[]> =>
        invoke('fs:read_dir', { path }),

    exists: (path: string): Promise<boolean> =>
        invoke('fs:exists', { path }),

    rename: (oldPath: string, newPath: string): Promise<void> =>
        invoke('fs:rename', { oldPath, newPath }),

    delete: (path: string): Promise<void> =>
        invoke('fs:delete', { path }),

    createDir: (path: string): Promise<void> =>
        invoke('fs:create_dir', { path }),
}
declare module '@dotcarbon/api' {
    interface CarbonCommands {
        'fs:read_file': { args: { path: string }; result: string }
        'fs:write_file': { args: { path: string; contents: string }; result: void }
        'fs:read_dir': { args: { path: string }; result: DirEntry[] }
        'fs:exists': { args: { path: string }; result: boolean }
        'fs:rename': { args: { oldPath: string; newPath: string }; result: void }
        'fs:delete': { args: { path: string }; result: void }
        'fs:create_dir': { args: { path: string }; result: void }
    }
}
