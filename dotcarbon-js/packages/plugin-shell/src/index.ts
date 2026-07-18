import { invoke } from '@dotcarbon/api'

export interface ExecuteOptions {
    program: string
    args?: string[]
    cwd?: string
    env?: Record<string, string>
}

export interface SidecarOptions {
    cwd?: string
    env?: Record<string, string>
}

export interface ShellResult {
    exitCode: number
    stdout: string
    stderr: string
    success: boolean
}

export const shell = {
    execute: (options: ExecuteOptions): Promise<ShellResult> =>
        invoke('shell:execute', {
            program: options.program,
            args: options.args ?? [],
            cwd: options.cwd ?? null,
            env: options.env ?? null,
        }),

    run: async (program: string, args: string[] = [], cwd?: string): Promise<string> => {
        const result = await invoke('shell:execute', {
            program,
            args,
            cwd: cwd ?? null,
            env: null,
        })
        if (!result.success)
            throw new Error(result.stderr || `Process exited with code ${result.exitCode}`)
        return result.stdout
    },

    // Runs a binary bundled next to the app (declared in bundle.externalBin). Equivalent to Tauri's
    // Command.sidecar(). The name is the same path prefix used in config, e.g. 'binaries/my-tool'.
    sidecar: (name: string, args: string[] = [], options: SidecarOptions = {}): Promise<ShellResult> =>
        invoke('shell:execute', {
            program: name,
            args,
            cwd: options.cwd ?? null,
            env: options.env ?? null,
            sidecar: true,
        }),

    open: (path: string): Promise<void> =>
        invoke('shell:open', { path }),

    openUrl: (url: string): Promise<void> =>
        invoke('shell:open_url', { path: url }),
}

declare module '@dotcarbon/api' {
    interface CarbonCommands {
        'shell:execute': { args: { program: string; args: string[]; cwd: string | null; env: Record<string, string> | null; sidecar?: boolean }; result: ShellResult }
        'shell:open': { args: { path: string }; result: void }
        'shell:open_url': { args: { path: string }; result: void }
    }
}
