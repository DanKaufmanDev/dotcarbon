import { invoke, listen, once, emit, type UnlistenFn, type CarbonEvent } from '@dotcarbon/api'

export interface WindowState {
    label: string
    title: string
    width: number
    height: number
    x: number
    y: number
    fullscreen: boolean
    maximized: boolean
    minimized: boolean
    alwaysOnTop: boolean
    resizable: boolean
    visible: boolean
    focused: boolean
}

export interface PhysicalSize { width: number; height: number }
export interface PhysicalPosition { x: number; y: number }
export interface LogicalSize { width: number; height: number }
export interface LogicalPosition { x: number; y: number }

export type Theme = 'light' | 'dark'

/**
 * Subscribe to theme changes via the webview's prefers-color-scheme, which follows both the OS theme
 * and a window setTheme override. Returns an unsubscribe function. Task 3.6.
 */
export function onThemeChanged(handler: (theme: Theme) => void): () => void {
    if (typeof window === 'undefined' || !window.matchMedia) return () => undefined
    const query = window.matchMedia('(prefers-color-scheme: dark)')
    const listener = (e: MediaQueryListEvent) => handler(e.matches ? 'dark' : 'light')
    query.addEventListener('change', listener)
    return () => query.removeEventListener('change', listener)
}

/** A connected display (Task 3.5). Sizes and positions are in physical pixels. */
export interface Monitor {
    name: string | null
    position: PhysicalPosition
    size: PhysicalSize
    workPosition: PhysicalPosition
    workSize: PhysicalSize
    scaleFactor: number
}

/** Convert a physical measurement to logical (CSS) pixels by dividing out the scale factor. */
export const toLogicalSize = (size: PhysicalSize, scaleFactor: number): LogicalSize =>
    ({ width: size.width / scaleFactor, height: size.height / scaleFactor })
export const toLogicalPosition = (pos: PhysicalPosition, scaleFactor: number): LogicalPosition =>
    ({ x: pos.x / scaleFactor, y: pos.y / scaleFactor })

export interface CreateWindowOptions {
    label: string
    url?: string
    parentLabel?: string
    capabilities?: string[]
    title?: string
    width?: number
    height?: number
    minWidth?: number
    minHeight?: number
    maxWidth?: number
    maxHeight?: number
    x?: number
    y?: number
    center?: boolean
    resizable?: boolean
    fullscreen?: boolean
    maximized?: boolean
    alwaysOnTop?: boolean
    decorations?: boolean
    transparent?: boolean
    devTools?: boolean
    contextMenu?: boolean
    icon?: string
}

export class WebviewWindow {
    constructor(public readonly label: string) {}

    static async create(options: CreateWindowOptions): Promise<WebviewWindow> {
        const state = await invoke('window:create', options)
        return new WebviewWindow(state.label)
    }

    static async getCurrent(): Promise<WebviewWindow> {
        const state = await invoke('window:get_state', {})
        return new WebviewWindow(state.label)
    }

    static async getByLabel(label: string): Promise<WebviewWindow | null> {
        const state = await invoke('window:get_by_label', { label })
        return state ? new WebviewWindow(state.label) : null
    }

    static async getAll(): Promise<WebviewWindow[]> {
        const states = await invoke('window:get_all')
        return states.map(state => new WebviewWindow(state.label))
    }

    /** The window that currently has focus, or null if none does. */
    static async getFocused(): Promise<WebviewWindow | null> {
        for (const window of await WebviewWindow.getAll()) {
            if (await window.isFocused()) return window
        }
        return null
    }

    /** Emit an event targeted at this window (Task 3.11 — per-window event scoping). */
    emit = <T>(event: string, payload: T): Promise<void> =>
        emit(event, payload, { target: { kind: 'window', label: this.label } })

    /** Listen for an event. Returns an unlisten function. */
    listen = <T = unknown>(event: string, handler: (event: CarbonEvent<T>) => void): Promise<UnlistenFn> =>
        listen(event, handler as never)

    /** Listen for the next occurrence of an event, then stop. */
    once = <T = unknown>(event: string, handler: (event: CarbonEvent<T>) => void): Promise<UnlistenFn> =>
        once(event, handler as never)

    getState = (): Promise<WindowState> =>
        invoke('window:get_state', { label: this.label })

    setTitle = (title: string): Promise<void> =>
        invoke('window:set_title', { title, label: this.label })

    setSize = (width: number, height: number): Promise<void> =>
        invoke('window:set_size', { width, height, label: this.label })

    setPosition = (x: number, y: number): Promise<void> =>
        invoke('window:set_position', { x, y, label: this.label })

    center = (): Promise<void> =>
        invoke('window:center', { label: this.label })

    minimize = (): Promise<void> =>
        invoke('window:minimize', { label: this.label })

    maximize = (): Promise<void> =>
        invoke('window:maximize', { label: this.label })

    unmaximize = (): Promise<void> =>
        invoke('window:unmaximize', { label: this.label })

    setFullscreen = (fullscreen: boolean): Promise<void> =>
        invoke('window:set_fullscreen', { fullscreen, label: this.label })

    setAlwaysOnTop = (alwaysOnTop: boolean): Promise<void> =>
        invoke('window:set_always_on_top', { alwaysOnTop, label: this.label })

    setResizable = (resizable: boolean): Promise<void> =>
        invoke('window:set_resizable', { resizable, label: this.label })

    close = (): Promise<void> =>
        invoke('window:close', { label: this.label })

    // Task 3.1 — visibility & focus.
    show = (): Promise<void> => invoke('window:show', { label: this.label })
    hide = (): Promise<void> => invoke('window:hide', { label: this.label })
    setFocus = (): Promise<void> => invoke('window:set_focus', { label: this.label })
    isVisible = (): Promise<boolean> => invoke('window:is_visible', { label: this.label })
    isFocused = (): Promise<boolean> => invoke('window:is_focused', { label: this.label })
    requestUserAttention = (): Promise<void> =>
        invoke('window:request_user_attention', { label: this.label })

    // Task 3.2 — geometry depth.
    setMinSize = (width: number, height: number): Promise<void> =>
        invoke('window:set_min_size', { width, height, label: this.label })
    setMaxSize = (width: number, height: number): Promise<void> =>
        invoke('window:set_max_size', { width, height, label: this.label })
    innerSize = (): Promise<PhysicalSize> => invoke('window:inner_size', { label: this.label })
    outerSize = (): Promise<PhysicalSize> => invoke('window:outer_size', { label: this.label })
    innerPosition = (): Promise<PhysicalPosition> =>
        invoke('window:inner_position', { label: this.label })
    outerPosition = (): Promise<PhysicalPosition> =>
        invoke('window:outer_position', { label: this.label })
    isMaximized = (): Promise<boolean> => invoke('window:is_maximized', { label: this.label })
    isMinimized = (): Promise<boolean> => invoke('window:is_minimized', { label: this.label })
    isFullscreen = (): Promise<boolean> => invoke('window:is_fullscreen', { label: this.label })

    // Task 3.8 — begin an OS window-move drag (for custom title bars).
    startDragging = (): Promise<void> => invoke('window:start_dragging', { label: this.label })

    // Task 3.3 — chrome & behavior.
    setDecorations = (decorations: boolean): Promise<void> =>
        invoke('window:set_decorations', { value: decorations, label: this.label })
    setClosable = (closable: boolean): Promise<void> =>
        invoke('window:set_closable', { value: closable, label: this.label })
    setMinimizable = (minimizable: boolean): Promise<void> =>
        invoke('window:set_minimizable', { value: minimizable, label: this.label })
    setMaximizable = (maximizable: boolean): Promise<void> =>
        invoke('window:set_maximizable', { value: maximizable, label: this.label })
    setAlwaysOnBottom = (alwaysOnBottom: boolean): Promise<void> =>
        invoke('window:set_always_on_bottom', { value: alwaysOnBottom, label: this.label })
    setSkipTaskbar = (skip: boolean): Promise<void> =>
        invoke('window:set_skip_taskbar', { value: skip, label: this.label })
    setContentProtected = (protectedContent: boolean): Promise<void> =>
        invoke('window:set_content_protected', { value: protectedContent, label: this.label })
    setIgnoreCursorEvents = (ignore: boolean): Promise<void> =>
        invoke('window:set_ignore_cursor_events', { value: ignore, label: this.label })
    setIcon = (path: string): Promise<void> =>
        invoke('window:set_icon', { path, label: this.label })

    // Task 3.4 — cursor.
    setCursorIcon = (icon: string): Promise<void> =>
        invoke('window:set_cursor_icon', { icon, label: this.label })
    setCursorVisible = (visible: boolean): Promise<void> =>
        invoke('window:set_cursor_visible', { value: visible, label: this.label })
    setCursorGrab = (grab: boolean): Promise<void> =>
        invoke('window:set_cursor_grab', { value: grab, label: this.label })
    setCursorPosition = (x: number, y: number): Promise<void> =>
        invoke('window:set_cursor_position', { x, y, label: this.label })

    // Task 3.5 — monitors.
    availableMonitors = (): Promise<Monitor[]> =>
        invoke('window:available_monitors', { label: this.label })
    primaryMonitor = (): Promise<Monitor | null> =>
        invoke('window:primary_monitor', { label: this.label })
    currentMonitor = (): Promise<Monitor | null> =>
        invoke('window:current_monitor', { label: this.label })
    scaleFactor = (): Promise<number> => invoke('window:scale_factor', { label: this.label })

    // Task 3.6 — theme.
    theme = (): Promise<Theme> => invoke('window:get_theme', { label: this.label })
    setTheme = (theme: Theme | 'auto'): Promise<void> =>
        invoke('window:set_theme', { theme, label: this.label })
    /**
     * Fire when the effective theme changes. Backed by the webview's prefers-color-scheme, so it
     * reflects both OS changes and a setTheme override. Returns an unsubscribe function.
     */
    onThemeChanged = (handler: (theme: Theme) => void): (() => void) => onThemeChanged(handler)

    // Task 3.7 — lifecycle events + close-requested veto.
    /** Close the window for real, bypassing any close interception. */
    destroy = (): Promise<void> => invoke('window:destroy', { label: this.label })

    /**
     * Intercept the window close. The handler runs before the window closes; call
     * `event.preventDefault()` to keep it open, otherwise it closes. Returns an unlisten function.
     */
    async onCloseRequested(handler: (event: { preventDefault: () => void }) => void): Promise<UnlistenFn> {
        await invoke('window:set_close_interception', { value: true, label: this.label })
        return listen('window:close-requested', (e) => {
            if ((e.payload as { label: string }).label !== this.label) return
            let prevented = false
            handler({ preventDefault: () => { prevented = true } })
            if (!prevented) invoke('window:destroy', { label: this.label }).catch(() => {})
        })
    }

    private onWindowEvent<T>(name: string, handler: (payload: T) => void): Promise<UnlistenFn> {
        return listen(name, (e) => {
            if ((e.payload as { label: string }).label === this.label) handler(e.payload as T)
        })
    }

    onFocusChanged = (handler: (focused: boolean) => void): Promise<UnlistenFn[]> => Promise.all([
        this.onWindowEvent('window:focus', () => handler(true)),
        this.onWindowEvent('window:blur', () => handler(false)),
    ])
    onMoved = (handler: (position: PhysicalPosition) => void): Promise<UnlistenFn> =>
        this.onWindowEvent<{ x: number; y: number }>('window:moved', p => handler({ x: p.x, y: p.y }))
    onResized = (handler: (size: PhysicalSize) => void): Promise<UnlistenFn> =>
        this.onWindowEvent<{ width: number; height: number }>('window:resized', p => handler({ width: p.width, height: p.height }))
    onMinimized = (handler: () => void): Promise<UnlistenFn> => this.onWindowEvent('window:minimized', handler)
    onMaximized = (handler: () => void): Promise<UnlistenFn> => this.onWindowEvent('window:maximized', handler)
    onRestored = (handler: () => void): Promise<UnlistenFn> => this.onWindowEvent('window:restored', handler)
    onCloseChanged = (handler: () => void): Promise<UnlistenFn> => this.onWindowEvent('window:closed', handler)

    // Task 3.9 — taskbar progress + dock badge.
    /** Set the taskbar progress bar (Windows). progress is 0–100. */
    setProgressBar = (progress: number, status: ProgressBarStatus = 'normal'): Promise<void> =>
        invoke('window:set_progress_bar', { progress, status, label: this.label })
    /** Set the app badge (macOS dock); null clears it. */
    setBadge = (value: string | null): Promise<void> =>
        invoke('window:set_badge', { value, label: this.label })

    /**
     * Task 3.10 — background effect. "vibrancy"/a material name (macOS), "mica"/"acrylic" (Windows),
     * or "none". The window must be transparent for it to show through the page.
     */
    setEffect = (effect: WindowEffect): Promise<void> =>
        invoke('window:set_effect', { effect, label: this.label })
}

export type WindowEffect =
    | 'none' | 'vibrancy' | 'sidebar' | 'menu' | 'popover' | 'hud' | 'fullscreen' | 'under-window'
    | 'mica' | 'acrylic' | 'tabbed' 

export type ProgressBarStatus = 'none' | 'normal' | 'indeterminate' | 'paused' | 'error'


export { WebviewWindow as CarbonWindow }

export const getCurrentWindow = WebviewWindow.getCurrent
export const getAllWindows = WebviewWindow.getAll
export const getWindowByLabel = WebviewWindow.getByLabel
export const createWindow = WebviewWindow.create

export const carbonWindow = {
    getState: (): Promise<WindowState> => invoke('window:get_state', {}),
    setTitle: (title: string): Promise<void> => invoke('window:set_title', { title }),
    setSize: (width: number, height: number): Promise<void> =>
        invoke('window:set_size', { width, height }),
    setPosition: (x: number, y: number): Promise<void> =>
        invoke('window:set_position', { x, y }),
    center: (): Promise<void> => invoke('window:center', {}),
    minimize: (): Promise<void> => invoke('window:minimize', {}),
    maximize: (): Promise<void> => invoke('window:maximize', {}),
    unmaximize: (): Promise<void> => invoke('window:unmaximize', {}),
    setFullscreen: (fullscreen: boolean): Promise<void> =>
        invoke('window:set_fullscreen', { fullscreen }),
    setAlwaysOnTop: (alwaysOnTop: boolean): Promise<void> =>
        invoke('window:set_always_on_top', { alwaysOnTop }),
    setResizable: (resizable: boolean): Promise<void> =>
        invoke('window:set_resizable', { resizable }),
    close: (): Promise<void> => invoke('window:close', {}),
    show: (): Promise<void> => invoke('window:show', {}),
    hide: (): Promise<void> => invoke('window:hide', {}),
    setFocus: (): Promise<void> => invoke('window:set_focus', {}),
    isVisible: (): Promise<boolean> => invoke('window:is_visible', {}),
    isFocused: (): Promise<boolean> => invoke('window:is_focused', {}),
    requestUserAttention: (): Promise<void> => invoke('window:request_user_attention', {}),
    setMinSize: (width: number, height: number): Promise<void> =>
        invoke('window:set_min_size', { width, height }),
    setMaxSize: (width: number, height: number): Promise<void> =>
        invoke('window:set_max_size', { width, height }),
    innerSize: (): Promise<PhysicalSize> => invoke('window:inner_size', {}),
    outerSize: (): Promise<PhysicalSize> => invoke('window:outer_size', {}),
    innerPosition: (): Promise<PhysicalPosition> => invoke('window:inner_position', {}),
    outerPosition: (): Promise<PhysicalPosition> => invoke('window:outer_position', {}),
    isMaximized: (): Promise<boolean> => invoke('window:is_maximized', {}),
    isMinimized: (): Promise<boolean> => invoke('window:is_minimized', {}),
    isFullscreen: (): Promise<boolean> => invoke('window:is_fullscreen', {}),
    startDragging: (): Promise<void> => invoke('window:start_dragging', {}),
    setDecorations: (decorations: boolean): Promise<void> => invoke('window:set_decorations', { value: decorations }),
    setClosable: (closable: boolean): Promise<void> => invoke('window:set_closable', { value: closable }),
    setMinimizable: (minimizable: boolean): Promise<void> => invoke('window:set_minimizable', { value: minimizable }),
    setMaximizable: (maximizable: boolean): Promise<void> => invoke('window:set_maximizable', { value: maximizable }),
    setAlwaysOnBottom: (alwaysOnBottom: boolean): Promise<void> => invoke('window:set_always_on_bottom', { value: alwaysOnBottom }),
    setSkipTaskbar: (skip: boolean): Promise<void> => invoke('window:set_skip_taskbar', { value: skip }),
    setContentProtected: (protectedContent: boolean): Promise<void> => invoke('window:set_content_protected', { value: protectedContent }),
    setIgnoreCursorEvents: (ignore: boolean): Promise<void> => invoke('window:set_ignore_cursor_events', { value: ignore }),
    setIcon: (path: string): Promise<void> => invoke('window:set_icon', { path }),
    setCursorIcon: (icon: string): Promise<void> => invoke('window:set_cursor_icon', { icon }),
    setCursorVisible: (visible: boolean): Promise<void> => invoke('window:set_cursor_visible', { value: visible }),
    setCursorGrab: (grab: boolean): Promise<void> => invoke('window:set_cursor_grab', { value: grab }),
    setCursorPosition: (x: number, y: number): Promise<void> => invoke('window:set_cursor_position', { x, y }),
    availableMonitors: (): Promise<Monitor[]> => invoke('window:available_monitors', {}),
    primaryMonitor: (): Promise<Monitor | null> => invoke('window:primary_monitor', {}),
    currentMonitor: (): Promise<Monitor | null> => invoke('window:current_monitor', {}),
    scaleFactor: (): Promise<number> => invoke('window:scale_factor', {}),
    theme: (): Promise<Theme> => invoke('window:get_theme', {}),
    setTheme: (theme: Theme | 'auto'): Promise<void> => invoke('window:set_theme', { theme }),
    onThemeChanged: (handler: (theme: Theme) => void): (() => void) => onThemeChanged(handler),
    setProgressBar: (progress: number, status: ProgressBarStatus = 'normal'): Promise<void> =>
        invoke('window:set_progress_bar', { progress, status }),
    setBadge: (value: string | null): Promise<void> => invoke('window:set_badge', { value }),
    setEffect: (effect: WindowEffect): Promise<void> => invoke('window:set_effect', { effect }),
}

declare module '@dotcarbon/api' {
    interface CarbonCommands {
        'window:create': { args: CreateWindowOptions; result: WindowState }
        'window:get_all': { args: void; result: WindowState[] }
        'window:get_by_label': { args: { label: string }; result: WindowState | null }
        'window:get_state': { args: { label?: string }; result: WindowState }
        'window:set_title': { args: { title: string; label?: string }; result: void }
        'window:set_size': { args: { width: number; height: number; label?: string }; result: void }
        'window:set_position': { args: { x: number; y: number; label?: string }; result: void }
        'window:center': { args: { label?: string }; result: void }
        'window:minimize': { args: { label?: string }; result: void }
        'window:show': { args: { label?: string }; result: void }
        'window:hide': { args: { label?: string }; result: void }
        'window:set_focus': { args: { label?: string }; result: void }
        'window:is_visible': { args: { label?: string }; result: boolean }
        'window:is_focused': { args: { label?: string }; result: boolean }
        'window:request_user_attention': { args: { label?: string }; result: void }
        'window:set_min_size': { args: { width: number; height: number; label?: string }; result: void }
        'window:set_max_size': { args: { width: number; height: number; label?: string }; result: void }
        'window:inner_size': { args: { label?: string }; result: PhysicalSize }
        'window:outer_size': { args: { label?: string }; result: PhysicalSize }
        'window:inner_position': { args: { label?: string }; result: PhysicalPosition }
        'window:outer_position': { args: { label?: string }; result: PhysicalPosition }
        'window:is_maximized': { args: { label?: string }; result: boolean }
        'window:is_minimized': { args: { label?: string }; result: boolean }
        'window:is_fullscreen': { args: { label?: string }; result: boolean }
        'window:start_dragging': { args: { label?: string }; result: void }
        'window:set_decorations': { args: { value: boolean; label?: string }; result: void }
        'window:set_closable': { args: { value: boolean; label?: string }; result: void }
        'window:set_minimizable': { args: { value: boolean; label?: string }; result: void }
        'window:set_maximizable': { args: { value: boolean; label?: string }; result: void }
        'window:set_always_on_bottom': { args: { value: boolean; label?: string }; result: void }
        'window:set_skip_taskbar': { args: { value: boolean; label?: string }; result: void }
        'window:set_content_protected': { args: { value: boolean; label?: string }; result: void }
        'window:set_ignore_cursor_events': { args: { value: boolean; label?: string }; result: void }
        'window:set_icon': { args: { path: string; label?: string }; result: void }
        'window:set_cursor_icon': { args: { icon: string; label?: string }; result: void }
        'window:set_cursor_visible': { args: { value: boolean; label?: string }; result: void }
        'window:set_cursor_grab': { args: { value: boolean; label?: string }; result: void }
        'window:set_cursor_position': { args: { x: number; y: number; label?: string }; result: void }
        'window:available_monitors': { args: { label?: string }; result: Monitor[] }
        'window:primary_monitor': { args: { label?: string }; result: Monitor | null }
        'window:current_monitor': { args: { label?: string }; result: Monitor | null }
        'window:scale_factor': { args: { label?: string }; result: number }
        'window:get_theme': { args: { label?: string }; result: Theme }
        'window:set_theme': { args: { theme: Theme | 'auto'; label?: string }; result: void }
        'window:set_close_interception': { args: { value: boolean; label?: string }; result: void }
        'window:destroy': { args: { label?: string }; result: void }
        'window:set_progress_bar': { args: { progress: number; status?: ProgressBarStatus; label?: string }; result: void }
        'window:set_badge': { args: { value: string | null; label?: string }; result: void }
        'window:set_effect': { args: { effect: WindowEffect; label?: string }; result: void }
        'window:maximize': { args: { label?: string }; result: void }
        'window:unmaximize': { args: { label?: string }; result: void }
        'window:set_fullscreen': { args: { fullscreen: boolean; label?: string }; result: void }
        'window:set_always_on_top': { args: { alwaysOnTop: boolean; label?: string }; result: void }
        'window:set_resizable': { args: { resizable: boolean; label?: string }; result: void }
        'window:close': { args: { label?: string }; result: void }
    }
}
