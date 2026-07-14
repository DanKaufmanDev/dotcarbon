// External script (not inline) so it satisfies the injected CSP `script-src 'self'` — the same
// posture a real Vite/webpack build ships. On boot it waits for the Carbon bridge, invokes the
// shared C# `app:greet` command, and shows the reply. The round-trip marker `[[CARBON_WEB_READY]]`
// is logged by the C# Greet command; the mobile smoke jobs assert it in logcat / the simulator log.
const pending = new Map();

function ensureReceiver() {
  if (!window.external || typeof window.external.receiveMessage !== "function") return false;
  if (ensureReceiver.ready) return true;
  window.external.receiveMessage((message) => {
    const result = JSON.parse(message);
    const settle = pending.get(result.id);
    if (!settle) return;
    pending.delete(result.id);
    result.ok ? settle.resolve(result.data) : settle.reject(new Error(String(result.data)));
  });
  ensureReceiver.ready = true;
  return true;
}

function invoke(command, payload) {
  if (!ensureReceiver() || typeof window.external.sendMessage !== "function") {
    return Promise.reject(new Error("Carbon bridge unavailable"));
  }
  const id =
    window.crypto && typeof window.crypto.randomUUID === "function"
      ? window.crypto.randomUUID()
      : String(Date.now());
  const request = { id, command, payload: payload || {} };
  return new Promise((resolve, reject) => {
    pending.set(id, { resolve, reject });
    window.external.sendMessage(JSON.stringify(request));
  });
}

function sleep(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

async function waitForBridge() {
  for (let i = 0; i < 50; i++) {
    if (window.external && typeof window.external.sendMessage === "function") return;
    await sleep(100);
  }
  throw new Error("Carbon bridge unavailable");
}

(async () => {
  try {
    await waitForBridge();
    const reply = await invoke("app:greet", { name: "CI" });
    console.log("[[CARBON_BRIDGE_OK]] " + reply);
    document.getElementById("out").textContent = reply;
  } catch (e) {
    console.log("[[CARBON_BRIDGE_ERR]] " + e);
  }
})();
