import { LitElement, css, html } from 'lit'
import { customElement, state } from 'lit/decorators.js'
import { invoke } from '@dotcarbon/api'

@customElement('carbon-app')
export class CarbonApp extends LitElement {
    static styles = css`
        main { font-family: system-ui; display: flex; flex-direction: column; align-items: center;
               justify-content: center; height: 100vh; background: #0f0f0f; color: white }
        h1 { color: #6366f1 }
        .muted { color: #888 }
        form { display: flex; gap: 8px; margin-top: 24px }
        input { padding: 10px 14px; border-radius: 8px; border: 1px solid #333;
                background: #1a1a1a; color: white; font-size: 15px }
        button { padding: 10px 24px; background: #6366f1; color: white; border: none;
                 border-radius: 8px; font-size: 15px; cursor: pointer }
        .msg { color: #6366f1; margin-top: 16px; min-height: 22px }
        .hint { color: #555; margin-top: 24px; font-size: 13px }
    `

    @state() private name = ''
    @state() private message = ''

    private async greet(event: Event) {
        event.preventDefault()
        this.message = await invoke('app:greet', { name: this.name })
    }

    render() {
        return html`
            <main>
                <h1>⚡ {{APP_NAME}}</h1>
                <p class="muted">Running on Carbon + Lit</p>
                <form @submit=${this.greet}>
                    <input placeholder="Enter a name..."
                        .value=${this.name}
                        @input=${(e: Event) => (this.name = (e.target as HTMLInputElement).value)} />
                    <button type="submit">Greet</button>
                </form>
                <p class="msg">${this.message}</p>
                <p class="hint">Edit <code>ui/src/main.ts</code> and <code>src-carbon/Program.cs</code></p>
            </main>
        `
    }
}
