import { invoke } from '@dotcarbon/api'

/** A matched argument: a boolean for a flag, a string or string[] for a value argument. */
export interface ArgMatch {
    value: boolean | string | string[] | null
    occurrences: number
}

/** The parsed command line, mirroring Tauri's getMatches(). */
export interface ArgMatches {
    args: Record<string, ArgMatch>
    subcommand: { name: string; matches: ArgMatches } | null
}

/** The parsed CLI arguments for this run, against the schema declared in plugins.cli. */
export const getMatches = (): Promise<ArgMatches> => invoke('cli:matches')

declare module '@dotcarbon/api' {
    interface CarbonCommands {
        'cli:matches': { args: void; result: ArgMatches }
    }
}
