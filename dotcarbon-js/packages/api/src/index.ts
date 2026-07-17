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

type BridgeChannel = {
    type: 'channel';
    id: number;
    message: unknown;
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

const channelHandlers = new Map<number, (message: unknown) => void>();
let nextChannelId = 0;

/**
 * A one-way stream from a command (Task 4.1). Create one, set `onmessage`, and pass it in a command's
 * arguments; the command sends messages that arrive here in order. Serializes to a channel marker the
 * backend recognizes.
 */
export class Channel<T> {
    readonly id = ++nextChannelId;
    onmessage: (message: T) => void = () => undefined;

    constructor() {
        channelHandlers.set(this.id, (message) => this.onmessage(message as T));
    }

    /** Stop receiving messages and release the handler. */
    close(): void {
        channelHandlers.delete(this.id);
    }

    toJSON(): { __carbon_channel__: number } {
        return { __carbon_channel__: this.id };
    }
}

let initialized = false;

function ensureInitialized() {
    if (initialized) return;
    initialized = true;

    (window.external as unknown as CarbonExternal).receiveMessage((message: string) => {
        const incoming: BridgeResult<unknown> | BridgeEvent | BridgeChannel = JSON.parse(message);
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

        if (incoming.type === 'channel') {
            const onmessage = channelHandlers.get(incoming.id);
            if (onmessage) {
                try { onmessage(incoming.message); }
                catch (error) { console.error(`[Carbon] Channel ${incoming.id} handler failed`, error); }
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

// --- drag regions (Task 3.8) -------------------------------------------------------------------
// A window with a transparent title bar or no decorations has no OS-draggable strip, so mark an
// element with `data-carbon-drag-region` and a primary-button mousedown on it moves the window. This
// is installed automatically (matching Tauri's data-tauri-drag-region) so apps get it for free.

let dragRegionsInstalled = false;

export function installDragRegions(): void {
    if (dragRegionsInstalled || typeof document === 'undefined') return;
    dragRegionsInstalled = true;

    document.addEventListener('mousedown', (event) => {
        if (event.button !== 0) return; // primary button only
        let el = event.target as Element | null;
        while (el) {
            if (el.hasAttribute?.('data-carbon-drag-region')) {
                // The command begins a native move loop from this very press.
                invoke('window:start_dragging').catch(() => {}); // no window plugin → ignore
                return;
            }
            el = el.parentElement;
        }
    });
}

if (typeof document !== 'undefined') {
    // The listener is cheap and only acts on drag-region elements, so installing it eagerly is safe
    // even for apps that never use one.
    installDragRegions();
}

// --- tray + native menu (desktop only) ---------------------------------------------------------
// These drive the tray and menu the app set up in C# with UseTray/UseMenu; the commands only exist
// when it did. `Menu.new` and `tray.setMenu` replace a menu wholesale, which is also how items are
// added or removed — no platform can splice an installed menu.

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
    /** The item's id, when it was given one. Frontend-declared items are matched on this. */
    id?: string
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
    /**
     * Replace the tray's menu, which is also how items are added or removed. The icon is untouched.
     * Items declared here have no C# handler, so their clicks arrive via `tray.onItem`.
     */
    setMenu: (items: MenuItemOptions[]): Promise<void> => invoke('tray:set_menu', { items }),
    /** Clicks on frontend-declared tray menu items. */
    onItem: (handler: (event: NativeItemEvent) => void): Promise<UnlistenFn> =>
        listen('tray:item_clicked', (e) => handler(e.payload as NativeItemEvent)),
}

/** A standard item the platform implements. Only Quit, CloseWindow and Minimize exist off macOS. */
export type MenuItemRole =
    | 'Quit' | 'About' | 'Services' | 'Copy' | 'Cut' | 'Paste' | 'SelectAll'
    | 'Undo' | 'Redo' | 'Minimize' | 'Zoom' | 'Hide' | 'HideOthers' | 'ShowAll' | 'CloseWindow'

/**
 * A menu item declared from the frontend. `items` makes it a submenu, `separator` a divider, and
 * `role` a predefined platform item. Give it an `id` to address it later and to recognise its clicks.
 */
export interface MenuItemOptions {
    id?: string
    label?: string
    separator?: boolean
    /** Present makes it a checkable item, and sets its initial state. */
    checked?: boolean
    enabled?: boolean
    role?: MenuItemRole
    shortcut?: string
    items?: MenuItemOptions[]
}

export interface MenuOptions {
    label: string
    items: MenuItemOptions[]
}

export const menu = {
    /** Grey an item out. Items are addressed by the `id` given when the menu was built. */
    setEnabled: (id: string, enabled: boolean): Promise<void> =>
        invoke('menu:set_enabled', { id, enabled }),
    /** Tick or untick a checkable item. */
    setChecked: (id: string, checked: boolean): Promise<void> =>
        invoke('menu:set_checked', { id, checked }),
    setLabel: (id: string, label: string): Promise<void> => invoke('menu:set_label', { id, label }),
    /** Clicks on items the C# side declared with `AddEventItem`. */
    onItem: (eventName: string, handler: (event: NativeItemEvent) => void): Promise<UnlistenFn> =>
        listen(eventName, (e) => handler(e.payload as NativeItemEvent)),
}

/**
 * Build a native menu from the frontend.
 *
 * `setAsAppMenu` replaces the whole menu — no platform can splice an installed one, so this is also
 * how items are added or removed. Ids from the previous menu stop resolving once it runs.
 *
 * Items declared here have no C# handler, so their clicks arrive via `Menu.onClick` carrying the
 * item's id.
 */
export const Menu = {
    new: (menus: MenuOptions[]) => ({
        /** Install these menus as the application menu, replacing whatever is there. */
        setAsAppMenu: (): Promise<void> => invoke('menu:set_app_menu', { menus }),
    }),
    /** Clicks on frontend-declared app menu items. */
    onClick: (handler: (event: NativeItemEvent) => void): Promise<UnlistenFn> =>
        listen('menu:item_clicked', (e) => handler(e.payload as NativeItemEvent)),
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
    'menu:set_app_menu': { args: { menus: MenuOptions[] }; result: void }
    'tray:set_menu': { args: { items: MenuItemOptions[] }; result: void }
}
