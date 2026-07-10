declare global {
    interface Window {
    }
}

type CarbonExternal = {
    sendMessage: (msg: string) => void;
    receiveMessage: (cb: (msg: string) => void) => void;
};

type BridgeResult<T> = {
    type?: 'response';
    id: string;
    ok: boolean;
    data: T;
};

type BridgeEvent = {
    type: 'event';
    id: number;
    event: string;
    payload: unknown;
    source?: string | null;
};

export interface CarbonCommands {}
export interface CarbonEvents {}

export interface CarbonEvent<T> {
    id: number;
    event: string;
    payload: T;
    source: string | null;
}

export type EventHandler<T> = (event: CarbonEvent<T>) => void;
export type UnlistenFn = () => void;
export type EventTarget =
    | 'all'
    | 'app'
    | string
    | { kind: 'all' | 'app' }
    | { kind: 'window'; label: string };

export interface EmitOptions {
    target?: EventTarget;
}

type Args<K extends keyof CarbonCommands> =
    CarbonCommands[K] extends { args: infer A } ? A : unknown;
type Result<K extends keyof CarbonCommands> =
    CarbonCommands[K] extends { result: infer R } ? R : unknown;

type InvokeParams<K extends string> = K extends keyof CarbonCommands
    ? (Args<K> extends void | undefined ? [] : [args: Args<K>])
    : [payload?: unknown];
type InvokeResult<K extends string> = K extends keyof CarbonCommands ? Result<K> : unknown;
type EventPayload<K extends string> = K extends keyof CarbonEvents ? CarbonEvents[K] : unknown;

const pending = new Map<string, (result: BridgeResult<unknown>) => void>();
const listeners = new Map<string, Map<number, EventHandler<unknown>>>();
let nextListenerId = 0;

let initialized = false;

function ensureInitialized() {
    if (initialized) return;
    initialized = true;

    (window.external as unknown as CarbonExternal).receiveMessage((message: string) => {
        const incoming: BridgeResult<unknown> | BridgeEvent = JSON.parse(message);
        if (incoming.type === 'event') {
            const event: CarbonEvent<unknown> = {
                id: incoming.id,
                event: incoming.event,
                payload: incoming.payload,
                source: incoming.source ?? null,
            };
            for (const handler of [...(listeners.get(incoming.event)?.values() ?? [])]) {
                try { handler(event); }
                catch (error) { console.error(`[Carbon] Event listener '${incoming.event}' failed`, error); }
            }
            return;
        }

        const result = incoming;
        const resolve = pending.get(result.id);
        if (resolve) {
            pending.delete(result.id);
            resolve(result);
        }
    });
}

export async function emit<K extends string>(
    event: K,
    payload: EventPayload<K>,
    options: EmitOptions = {},
): Promise<void> {
    await invoke('__carbon:event_emit', {
        event,
        payload,
        target: options.target ?? 'all',
    });
}

export async function listen<K extends string>(
    event: K,
    handler: EventHandler<EventPayload<K>>,
): Promise<UnlistenFn> {
    return addListener(event, handler as EventHandler<unknown>);
}

export async function once<K extends string>(
    event: K,
    handler: EventHandler<EventPayload<K>>,
): Promise<UnlistenFn> {
    let stop: UnlistenFn = () => undefined;
    stop = addListener(event, value => {
        stop();
        handler(value as CarbonEvent<EventPayload<K>>);
    });
    return stop;
}

export function unlisten(event: string, listenerId?: number): void {
    const eventListeners = listeners.get(event);
    if (!eventListeners) return;
    if (listenerId === undefined) eventListeners.clear();
    else eventListeners.delete(listenerId);
    if (eventListeners.size === 0) listeners.delete(event);
}

function addListener(event: string, handler: EventHandler<unknown>): UnlistenFn {
    ensureInitialized();
    const id = ++nextListenerId;
    let eventListeners = listeners.get(event);
    if (!eventListeners) listeners.set(event, eventListeners = new Map());
    eventListeners.set(id, handler);
    return () => unlisten(event, id);
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
