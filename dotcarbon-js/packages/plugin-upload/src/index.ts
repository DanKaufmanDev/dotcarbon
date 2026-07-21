import { invoke, listen } from '@dotcarbon/api'

export interface ProgressPayload {
    id: number
    progress: number
    total: number
}

export type ProgressHandler = (progress: ProgressPayload) => void

let nextId = 0

/** Upload a local file to a URL; returns the server's response body. */
export async function upload(
    url: string,
    filePath: string,
    onProgress?: ProgressHandler,
    headers?: Record<string, string>,
    method?: string,
): Promise<string> {
    const id = ++nextId
    const unlisten = onProgress
        ? await listen('upload:progress', ({ payload }) => {
            if (payload.id === id) onProgress(payload)
        })
        : undefined
    try {
        return await invoke('upload:upload', { url, filePath, id, headers: headers ?? null, method: method ?? null })
    } finally {
        unlisten?.()
    }
}

/** Download a URL to a local file. */
export async function download(
    url: string,
    filePath: string,
    onProgress?: ProgressHandler,
    headers?: Record<string, string>,
): Promise<void> {
    const id = ++nextId
    const unlisten = onProgress
        ? await listen('download:progress', ({ payload }) => {
            if (payload.id === id) onProgress(payload)
        })
        : undefined
    try {
        await invoke('upload:download', { url, filePath, id, headers: headers ?? null })
    } finally {
        unlisten?.()
    }
}

declare module '@dotcarbon/api' {
    interface CarbonCommands {
        'upload:upload': { args: { url: string; filePath: string; id: number; headers: Record<string, string> | null; method: string | null }; result: string }
        'upload:download': { args: { url: string; filePath: string; id: number; headers: Record<string, string> | null }; result: void }
    }
    interface CarbonEvents {
        'upload:progress': ProgressPayload
        'download:progress': ProgressPayload
    }
}
