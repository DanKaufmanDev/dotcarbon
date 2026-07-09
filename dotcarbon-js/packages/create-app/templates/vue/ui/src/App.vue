<script setup lang="ts">
import { ref } from 'vue'
import { invoke } from '@dotcarbon/api'

const name = ref('')
const greeting = ref('')

async function greet() {
    greeting.value = await invoke('app:greet', { name: name.value })
}
</script>

<template>
    <main class="app">
        <h1 class="title">⚡ {{APP_NAME}}</h1>
        <p class="subtitle">Running on Carbon + Vue</p>

        <form class="row" @submit.prevent="greet">
            <input v-model="name" class="input" placeholder="Enter a name..." />
            <button type="submit" class="btn">Greet</button>
        </form>

        <p class="greeting">{{ greeting }}</p>

        <p class="hint">Edit <code>ui/src/App.vue</code> and <code>src-carbon/Program.cs</code></p>
    </main>
</template>

<style scoped>
.app {
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
.title { color: #6366f1; }
.subtitle { color: #888; }
.row { display: flex; gap: 8px; margin-top: 24px; }
.input {
    padding: 10px 14px;
    border-radius: 8px;
    border: 1px solid #333;
    background: #1a1a1a;
    color: white;
    font-size: 15px;
}
.btn {
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
