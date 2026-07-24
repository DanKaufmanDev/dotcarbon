<script lang="ts">
    import { invoke } from '@dotcarbon/api'

    let name = ''
    let message = ''

    async function greet() {
        message = await invoke('app:greet', { name })
    }
</script>

<main>
    <h1>⚡ {{APP_NAME}}</h1>
    <p class="muted">Running on Carbon + SvelteKit</p>
    <form on:submit|preventDefault={greet}>
        <input placeholder="Enter a name..." bind:value={name} />
        <button type="submit">Greet</button>
    </form>
    <p class="msg">{message}</p>
    <p class="hint">Edit <code>ui/src/routes/+page.svelte</code> and <code>src-carbon/Program.cs</code></p>
</main>

<style>
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
</style>
