import { Component } from '@angular/core'
import { FormsModule } from '@angular/forms'
import { invoke } from '@dotcarbon/api'

@Component({
    selector: 'app-root',
    standalone: true,
    imports: [FormsModule],
    styles: [`
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
    `],
    template: `
        <main>
            <h1>⚡ {{APP_NAME}}</h1>
            <p class="muted">Running on Carbon + Angular</p>
            <form (submit)="greet($event)">
                <input placeholder="Enter a name..." [(ngModel)]="name" name="name" />
                <button type="submit">Greet</button>
            </form>
            <p class="msg">{{ message }}</p>
            <p class="hint">Edit <code>ui/src/app/app.component.ts</code> and <code>src-carbon/Program.cs</code></p>
        </main>
    `,
})
export class AppComponent {
    name = ''
    message = ''

    async greet(event: Event) {
        event.preventDefault()
        this.message = await invoke('app:greet', { name: this.name })
    }
}
