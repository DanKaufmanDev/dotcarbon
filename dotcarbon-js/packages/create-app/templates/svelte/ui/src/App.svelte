<script lang="ts">
    import { invoke } from '@dotcarbon/api'

    let name = ''
    let greeting = ''

    async function greet() {
        greeting = await invoke('app:greet', { name })
    }
</script>

<main>
    <h1>⚡ {{APP_NAME}}</h1>
    <p class="subtitle">Running on Carbon + Svelte</p>

    <form class="row" on:submit|preventDefault={greet}>
        <input bind:value={name} placeholder="Enter a name..." />
        <button type="submit">Greet</button>
    </form>

    <p class="greeting">{greeting}</p>

    <p class="hint">Edit <code>ui/src/App.svelte</code> and <code>src-carbon/Program.cs</code></p>
</main>

<style>
    main {
        font-family: system-ui;
        display: flex;
        flex-direction: column;
        align-items: center;
        justify-content: center;
        height: 100vh;
        background: #0f0f0f;
        color: white;
        margin: 0;
    }
    h1 { color: #6366f1; }
    .subtitle { color: #888; }
    .row { display: flex; gap: 8px; margin-top: 24px; }
    input {
        padding: 10px 14px;
        border-radius: 8px;
        border: 1px solid #333;
        background: #1a1a1a;
        color: white;
        font-size: 15px;
    }
    button {
        padding: 10px 24px;
        background: #6366f1;
        color: white;
        border: none;
        border-radius: 8px;
        font-size: 15px;
        cursor: pointer;
    }
    .greeting { color: #6366f1; margin-top: 16px; min-height: 22px; }
    .hint { color: #555; margin-top: 24px; font-size: 13px; }
</style>
