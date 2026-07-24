<script setup lang="ts">
import { invoke } from '@dotcarbon/api'

const name = ref('')
const message = ref('')

async function greet() {
    message.value = await invoke('app:greet', { name: name.value })
}
</script>

<template>
    <main>
        <h1>⚡ {{APP_NAME}}</h1>
        <p class="muted">Running on Carbon + Nuxt</p>
        <form @submit.prevent="greet">
            <input v-model="name" placeholder="Enter a name..." />
            <button type="submit">Greet</button>
        </form>
        <p class="msg">{{ message }}</p>
        <p class="hint">Edit <code>ui/app.vue</code> and <code>src-carbon/Program.cs</code></p>
    </main>
</template>

<style>
main { font-family: system-ui; display: flex; flex-direction: column; align-items: center;
       justify-content: center; height: 100vh; background: #0f0f0f; color: white; margin: 0 }
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
