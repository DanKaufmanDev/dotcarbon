import { invoke } from '@dotcarbon/api'

const app = document.querySelector<HTMLDivElement>('#app')!

app.innerHTML = `
  <main style="font-family:system-ui;display:flex;flex-direction:column;align-items:center;justify-content:center;height:100vh;background:#0f0f0f;color:white;margin:0">
    <h1 style="color:#6366f1">⚡ {{APP_NAME}}</h1>
    <p style="color:#888">Running on Carbon + Vanilla TS</p>
    <form id="greet-form" style="display:flex;gap:8px;margin-top:24px">
      <input id="greet-input" placeholder="Enter a name..."
        style="padding:10px 14px;border-radius:8px;border:1px solid #333;background:#1a1a1a;color:white;font-size:15px" />
      <button type="submit"
        style="padding:10px 24px;background:#6366f1;color:white;border:none;border-radius:8px;font-size:15px;cursor:pointer">Greet</button>
    </form>
    <p id="greet-msg" style="color:#6366f1;margin-top:16px;min-height:22px"></p>
    <p style="color:#555;margin-top:24px;font-size:13px">Edit <code>ui/src/main.ts</code> and <code>src-carbon/Program.cs</code></p>
  </main>
`

const form = app.querySelector<HTMLFormElement>('#greet-form')!
const input = app.querySelector<HTMLInputElement>('#greet-input')!
const msg = app.querySelector<HTMLParagraphElement>('#greet-msg')!

form.addEventListener('submit', async (e) => {
    e.preventDefault()
    msg.textContent = await invoke('app:greet', { name: input.value })
})
