import { invoke } from '@dotcarbon/api'

export interface ExecuteOptions {
    program: string
    args?: string[]
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
        const result = await invoke<ShellResult>('shell:execute', {
            program,
            args,
            cwd: cwd ?? null,
            env: null,
        })
        if (!result.success)
            throw new Error(result.stderr || `Process exited with code ${result.exitCode}`)
        return result.stdout
    },

    open: (path: string): Promise<void> =>
        invoke('shell:open', { path }),

    openUrl: (url: string): Promise<void> =>
        invoke('shell:open_url', { path: url }),
}