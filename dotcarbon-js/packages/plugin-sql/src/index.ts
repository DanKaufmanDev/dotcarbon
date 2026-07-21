import { invoke } from '@dotcarbon/api'

export interface ExecuteResult {
    rowsAffected: number
    lastInsertId: number
}

/**
 * A handle to a loaded SQLite database. Get one from load(), then run queries. Values bind to $1, $2,
 * … placeholders. Mirrors Tauri's SQL plugin.
 */
export class Database {
    private constructor(readonly db: string) {}

    /** Open (or create) a database and apply any configured migrations. */
    static async load(db: string): Promise<Database> {
        const name = await invoke('sql:load', { db })
        return new Database(name)
    }

    /** Run a write query (INSERT/UPDATE/DELETE/DDL). */
    execute(query: string, values: unknown[] = []): Promise<ExecuteResult> {
        return invoke('sql:execute', { db: this.db, query, values })
    }

    /** Run a read query; returns each row as an object. */
    select<T = Record<string, unknown>>(query: string, values: unknown[] = []): Promise<T[]> {
        return invoke('sql:select', { db: this.db, query, values }) as Promise<T[]>
    }

    /** Close the database. */
    close(): Promise<void> {
        return invoke('sql:close', { db: this.db })
    }
}

declare module '@dotcarbon/api' {
    interface CarbonCommands {
        'sql:load': { args: { db: string }; result: string }
        'sql:execute': { args: { db: string; query: string; values: unknown[] }; result: ExecuteResult }
        'sql:select': { args: { db: string; query: string; values: unknown[] }; result: Record<string, unknown>[] }
        'sql:close': { args: { db: string }; result: void }
    }
}
