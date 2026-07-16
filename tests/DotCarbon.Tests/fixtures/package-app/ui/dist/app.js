// External script (not inline) so it satisfies the injected CSP `script-src 'self'` — the same
// posture a real Vite build ships. Drives the Task 2.10 tray/menu commands over the bridge the way
// @dotcarbon/api's `tray` and `menu` helpers do, then reports back with an event so the C# side can
// log a marker the smoke asserts. Written against the raw bridge because the fixture has no bundler.
const pending = new Map();

function ensureReceiver() {
  if (!window.external || typeof window.external.receiveMessage !== "function") return false;
  if (ensureReceiver.ready) return true;
  window.external.receiveMessage((message) => {
    const result = JSON.parse(message);
    if (result.type === "event") return;
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
  return new Promise((resolve, reject) => {
    pending.set(id, { resolve, reject });
    window.external.sendMessage(JSON.stringify({ id, command, payload: payload || {} }));
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
    // Exactly what @dotcarbon/api's tray.setTooltip / menu.setLabel / menu.setEnabled send.
    await invoke("tray:set_tooltip", { tooltip: "Tooltip from JS" });
    await invoke("tray:set_title", { title: "JS" });
    await invoke("menu:set_label", { id: "about", label: "About (from JS)" });
    await invoke("menu:set_enabled", { id: "about", enabled: true });
    await invoke("menu:set_checked", { id: "verbose", checked: false });
    // Task 2.11: replace the whole app menu with one declared here — what Menu.new().setAsAppMenu()
    // sends. The item set is deliberately different from the C# one so a rebuild is provable.
    await invoke("menu:set_app_menu", {
      menus: [
        {
          label: "FromJS",
          items: [
            { id: "js-one", label: "JS One" },
            { id: "js-check", label: "JS Check", checked: true },
            { separator: true },
            {
              id: "js-sub",
              label: "JS Submenu",
              items: [{ id: "js-nested", label: "JS Nested" }],
            },
            { role: "Quit" },
          ],
        },
      ],
    });
    // Task 2.11: same for the tray menu.
    await invoke("tray:set_menu", {
      items: [
        { id: "js-tray-one", label: "JS Tray One" },
        { separator: true },
        { id: "js-tray-two", label: "JS Tray Two" },
      ],
    });
    // The invokes above resolve only if the native side accepted them, so reporting success here
    // means the whole JS -> bridge -> plugin -> native path ran.
    await invoke("__carbon:event_emit", {
      event: "smoke:ui_ok",
      payload: "tray+menu+rebuild",
      target: "all",
    });
    document.getElementById("out").textContent = "tray + menu driven from JS";
  } catch (e) {
    await invoke("__carbon:event_emit", {
      event: "smoke:ui_err",
      payload: String(e),
      target: "all",
    }).catch(() => {});
  }
})();
