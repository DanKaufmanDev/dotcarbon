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