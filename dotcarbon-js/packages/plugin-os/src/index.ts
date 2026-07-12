import { invoke } from '@dotcarbon/api'

export interface OsInfo {
    platform: string
    arch: string
    version: string
    family: string
    hostname: string
    exeExtension: string
    eol: string
}

export const os = {
    info: (): Promise<OsInfo> => invoke('os:info'),
    platform: (): Promise<string> => invoke('os:platform'),
    arch: (): Promise<string> => invoke('os:arch'),
    version: (): Promise<string> => invoke('os:version'),
    family: (): Promise<string> => invoke('os:family'),
    hostname: (): Promise<string> => invoke('os:hostname'),
}

declare module '@dotcarbon/api' {
    interface CarbonCommands {
        'os:info': { args: void; result: OsInfo }
        'os:platform': { args: void; result: string }
        'os:arch': { args: void; result: string }
        'os:version': { args: void; result: string }
        'os:family': { args: void; result: string }
        'os:hostname': { args: void; result: string }
    }
}
