declare global {
    interface Window {
    }
}

type CarbonExternal = {
    sendMessage: (msg: string) => void;
    receiveMessage: (cb: (msg: string) => void) => void;
};

type BridgeResult<T> = {
    id: string;
    ok: boolean;
    data: T;
};

export interface CarbonCommands {}

type Args<K extends keyof CarbonCommands> =
    CarbonCommands[K] extends { args: infer A } ? A : unknown;
type Result<K extends keyof CarbonCommands> =
    CarbonCommands[K] extends { result: infer R } ? R : unknown;

type InvokeParams<K extends string> = K extends keyof CarbonCommands
    ? (Args<K> extends void | undefined ? [] : [args: Args<K>])
    : [payload?: unknown];
type InvokeResult<K extends string> = K extends keyof CarbonCommands ? Result<K> : unknown;

const pending = new Map<string, (result: BridgeResult<unknown>) => void>();

let initialized = false;

function ensureInitialized() {
    if (initialized) return;
    initialized = true;

    (window.external as unknown as CarbonExternal).receiveMessage((message: string) => {
        const result: BridgeResult<unknown> = JSON.parse(message);
        const resolve = pending.get(result.id);
        if (resolve) {
            pending.delete(result.id);
            resolve(result);
        }
    });
}

export function invoke<K extends string>(
    command: K,
    ...params: InvokeParams<K>
): Promise<InvokeResult<K>>;
export async function invoke(command: string, ...params: [unknown?]): Promise<unknown> {
    ensureInitialized();

    const payload = params[0] ?? {};
    const id = crypto.randomUUID();

    const result = await new Promise<BridgeResult<unknown>>((resolve) => {
        pending.set(id, resolve);
        (window.external as unknown as CarbonExternal).sendMessage(JSON.stringify({ id, command, payload }));
    });

    if (!result.ok) throw new Error(String(result.data));
    return result.data;
}

export function isCarbonApp(): boolean {
    return typeof window !== 'undefined' &&
        typeof (window.external as { sendMessage?: unknown } | undefined)?.sendMessage === 'function';
}
