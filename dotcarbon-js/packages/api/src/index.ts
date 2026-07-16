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

export interface CarbonCommandMetadata {
    name: string;
    fullName: string;
    arguments: string | null;
    result: string | null;
}

export interface CarbonPermissionMetadata {
    identifier: string;
    description: string | null;
    commands: readonly string[];
}

export interface CarbonEventMetadata {
    name: string;
    payloadType: string | null;
    description: string | null;
}

export interface CarbonPluginMetadata {
    namespace: string;
    name: string;
    version: string | null;
    description: string | null;
    platforms: readonly string[];
    commands: readonly CarbonCommandMetadata[];
    permissions: readonly CarbonPermissionMetadata[];
    events: readonly CarbonEventMetadata[];
}

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

// --- tray + native menu (desktop only) ---------------------------------------------------------
// These control the tray and menu the app declared in C# with UseTray/UseMenu; the commands only
// exist when it did. Building a tray or menu from here is not supported yet — that needs the native
// backends to add and remove items at runtime.

/** What the pointer did to the tray icon. Platform support varies — see `TrayIcon.onEvent`. */
export type TrayEventKind = 'Click' | 'DoubleClick' | 'Enter' | 'Move' | 'Leave'
export type TrayMouseButton = 'Left' | 'Right' | 'Middle'
export type TrayButtonState = 'Up' | 'Down'

export interface TrayEvent {
    kind: TrayEventKind
    button: TrayMouseButton
    buttonState: TrayButtonState
    position: { x: number; y: number }
    rect: { x: number; y: number; width: number; height: number }
}

/** An item in the tray or app menu was chosen. */
export interface NativeItemEvent {
    label: string
    /** `tray` or `menu`. */
    kind: string
}

export const tray = {
    /**
     * Swap the icon. PNG works on macOS and Linux; Windows prefers `.ico` but decodes others.
     * `isTemplate` is macOS-only and makes the icon follow light/dark menu bars.
     */
    setIcon: (path: string, isTemplate = false): Promise<void> =>
        invoke('tray:set_icon', { path, isTemplate }),
    /** macOS only: Windows and Linux trays are icon-only. */
    setTitle: (title: string): Promise<void> => invoke('tray:set_title', { title }),
    /** Ignored on Linux when the tray is a StatusNotifierItem — the panel owns the icon. */
    setTooltip: (tooltip: string): Promise<void> => invoke('tray:set_tooltip', { tooltip }),
    setVisible: (visible: boolean): Promise<void> => invoke('tray:set_visible', { visible }),
    remove: (): Promise<void> => invoke('tray:remove'),
    /**
     * Pointer events on the icon, if the C# side forwarded them with `OnEvent(eventName)`.
     * macOS reports all five kinds; Windows omits Enter and Leave; Linux reports none when the tray
     * is a StatusNotifierItem, which is the default there.
     */
    onEvent: (eventName: string, handler: (event: TrayEvent) => void): Promise<UnlistenFn> =>
        listen(eventName, (e) => handler(e.payload as TrayEvent)),
}

export const menu = {
    /** Grey an item out. Items are addressed by the `id` given when the menu was built in C#. */
    setEnabled: (id: string, enabled: boolean): Promise<void> =>
        invoke('menu:set_enabled', { id, enabled }),
    /** Tick or untick an item added with `AddCheckItem`. */
    setChecked: (id: string, checked: boolean): Promise<void> =>
        invoke('menu:set_checked', { id, checked }),
    setLabel: (id: string, label: string): Promise<void> => invoke('menu:set_label', { id, label }),
    /** Clicks on items the C# side declared with `AddEventItem`. */
    onItem: (eventName: string, handler: (event: NativeItemEvent) => void): Promise<UnlistenFn> =>
        listen(eventName, (e) => handler(e.payload as NativeItemEvent)),
}

// Declared here rather than through `declare module '@dotcarbon/api'` — that form is for other
// packages augmenting this one; within the module itself the interface merges directly.
export interface CarbonCommands {
    'tray:set_icon': { args: { path: string; isTemplate?: boolean }; result: void }
    'tray:set_title': { args: { title: string }; result: void }
    'tray:set_tooltip': { args: { tooltip: string }; result: void }
    'tray:set_visible': { args: { visible: boolean }; result: void }
    'tray:remove': { args: void; result: void }
    'menu:set_enabled': { args: { id: string; enabled: boolean }; result: void }
    'menu:set_checked': { args: { id: string; checked: boolean }; result: void }
    'menu:set_label': { args: { id: string; label: string }; result: void }
}
