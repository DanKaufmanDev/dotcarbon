// A minimal stand-in for `@dotcarbon/api`'s invoke(), inlined so the example needs no build step.
// In a real app: `import { invoke } from '@dotcarbon/api'`.
const pending = new Map();
function ready() {
  if (!window.external || typeof window.external.receiveMessage !== "function") return false;
  if (ready.done) return true;
  window.external.receiveMessage((raw) => {
    const msg = JSON.parse(raw);
    const settle = pending.get(msg.id);
    if (!settle) return;
    pending.delete(msg.id);
    msg.ok ? settle.resolve(msg.data) : settle.reject(new Error(String(msg.data)));
  });
  ready.done = true;
  return true;
}
export function invoke(command, payload) {
  if (!ready() || typeof window.external.sendMessage !== "function")
    return Promise.reject(new Error("Carbon bridge unavailable"));
  const id = (window.crypto?.randomUUID?.() ?? String(Date.now() + Math.random()));
  return new Promise((resolve, reject) => {
    pending.set(id, { resolve, reject });
    window.external.sendMessage(JSON.stringify({ id, command, payload: payload || {} }));
  });
}

const DB = "sqlite:todos.db";
const list = document.getElementById("list");

async function open() {
  await invoke("sql:load", { db: DB });
  await invoke("sql:execute", {
    db: DB,
    query: "CREATE TABLE IF NOT EXISTS todos (id INTEGER PRIMARY KEY AUTOINCREMENT, text TEXT NOT NULL, done INTEGER NOT NULL DEFAULT 0)",
    values: [],
  });
}

async function refresh() {
  const rows = await invoke("sql:select", { db: DB, query: "SELECT id, text, done FROM todos ORDER BY id", values: [] });
  list.innerHTML = "";
  for (const row of rows) {
    const li = document.createElement("li");
    if (row.done) li.className = "done";
    const box = document.createElement("input");
    box.type = "checkbox"; box.checked = !!row.done;
    box.onchange = () => toggle(row.id, box.checked);
    const label = document.createElement("span"); label.textContent = row.text;
    const del = document.createElement("button"); del.className = "del"; del.textContent = "✕";
    del.onclick = () => remove(row.id);
    li.append(box, label, del); list.appendChild(li);
  }
}

async function add(text) {
  await invoke("sql:execute", { db: DB, query: "INSERT INTO todos (text) VALUES ($1)", values: [text] });
  await refresh();
}
async function toggle(id, done) {
  await invoke("sql:execute", { db: DB, query: "UPDATE todos SET done = $1 WHERE id = $2", values: [done ? 1 : 0, id] });
  await refresh();
}
async function remove(id) {
  await invoke("sql:execute", { db: DB, query: "DELETE FROM todos WHERE id = $1", values: [id] });
  await refresh();
}

document.getElementById("add").addEventListener("submit", async (e) => {
  e.preventDefault();
  const input = document.getElementById("text");
  if (input.value.trim()) { await add(input.value.trim()); input.value = ""; }
});

open().then(refresh).catch((err) => { list.innerHTML = `<li>${err}</li>`; });
