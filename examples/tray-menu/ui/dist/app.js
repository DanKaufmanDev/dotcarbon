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

invoke("app:greet", { name: "world" })
  .then((reply) => { document.getElementById("msg").textContent = reply; })
  .catch((err) => { document.getElementById("msg").textContent = String(err); });
