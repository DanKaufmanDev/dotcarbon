import { invoke } from '@dotcarbon/api'

export interface OpenFileOptions {
    title?: string
    defaultPath?: string
    multiple?: boolean
    filters?: string[]
}

export interface SaveFileOptions {
    title?: string
    defaultPath?: string
    defaultName?: string
    filters?: string[]
}

export interface MessageOptions {
    title?: string
    message: string
    kind?: 'info' | 'warning' | 'error'
}

export interface ConfirmOptions {
    title?: string
    message: string
}

export const dialog = {
    openFile: (options: OpenFileOptions = {}): Promise<string[] | null> =>
        invoke('dialog:open_file', {
            title: options.title ?? 'Open File',
            defaultPath: options.defaultPath ?? null,
            multiple: options.multiple ?? false,
            filters: options.filters ?? null,
        }),

    saveFile: (options: SaveFileOptions = {}): Promise<string | null> =>
        invoke('dialog:save_file', {
            title: options.title ?? 'Save File',
            defaultPath: options.defaultPath ?? null,
            defaultName: options.defaultName ?? null,
            filters: options.filters ?? null,
        }),

    openFolder: (options: { title?: string; defaultPath?: string } = {}): Promise<string | null> =>
        invoke('dialog:open_folder', {
            title: options.title ?? 'Select Folder',
            defaultPath: options.defaultPath ?? null,
        }),

    message: (message: string, options: Omit<MessageOptions, 'message'> = {}): Promise<void> =>
        invoke('dialog:message', {
            title: options.title ?? 'Message',
            message,
            kind: options.kind ?? 'info',
        }),

    confirm: (message: string, options: { title?: string } = {}): Promise<boolean> =>
        invoke('dialog:confirm', {
            title: options.title ?? 'Confirm',
            message,
        }),
}