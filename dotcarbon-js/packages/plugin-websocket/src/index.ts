import { invoke, listen, type UnlistenFn } from '@dotcarbon/api'

export interface WsMessage {
    id: number
    type: 'text' | 'binary' | 'close' | 'error'
    data: string
}

/**
 * A client websocket connection proxied through the backend, bypassing the webview's mixed-content and
 * CORS limits. Get one from connect(), listen for frames, send, then disconnect. Mirrors Tauri.
 */
export class WebSocket {
    private constructor(readonly id: number) {}

    /** Open a connection to a ws:// or wss:// URL. */
    static async connect(url: string): Promise<WebSocket> {
        const id = await invoke('websocket:connect', { url })
        return new WebSocket(id)
    }

    /** Receive frames for this connection. Returns a detach function. */
    addListener(handler: (message: WsMessage) => void): Promise<UnlistenFn> {
        return listen('websocket:message', ({ payload }) => {
            if (payload.id === this.id) handler(payload)
        })
    }

    /** Send a text frame. */
    send(data: string): Promise<void> {
        return invoke('websocket:send', { id: this.id, data })
    }

    /** Close the connection. */
    disconnect(): Promise<void> {
        return invoke('websocket:disconnect', { id: this.id })
    }
}

declare module '@dotcarbon/api' {
    interface CarbonCommands {
        'websocket:connect': { args: { url: string }; result: number }
        'websocket:send': { args: { id: number; data: string }; result: void }
        'websocket:disconnect': { args: { id: number }; result: void }
    }
    interface CarbonEvents {
        'websocket:message': WsMessage
    }
}
