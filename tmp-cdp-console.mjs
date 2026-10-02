const http = await import("node:http");

function getJson(url) {
  return new Promise((res, rej) => {
    http.get(url, r => {
      let d = "";
      r.on("data", c => d += c);
      r.on("end", () => { try { res(JSON.parse(d)); } catch (e) { rej(e); } });
    }).on("error", rej);
  });
}

const list = await getJson("http://127.0.0.1:8765/json/list");
const page = list.find(t => t.type === "page" && t.url.includes("index.html"));
if (!page) { console.error("no page target"); process.exit(1); }

const ws = new WebSocket(page.webSocketDebuggerUrl);
let msgId = 0;
const pending = new Map();

function send(method, params = {}) {
  return new Promise((resolve, reject) => {
    const id = ++msgId;
    pending.set(id, { resolve, reject });
    ws.send(JSON.stringify({ id, method, params }));
  });
}

ws.onmessage = ev => {
  const msg = JSON.parse(ev.data);
  if (msg.id && pending.has(msg.id)) {
    const p = pending.get(msg.id);
    pending.delete(msg.id);
    msg.error ? p.reject(new Error(JSON.stringify(msg.error))) : p.resolve(msg.result);
    return;
  }
  if (msg.method === "Runtime.consoleAPICalled") {
    const parts = (msg.params.args || []).map(a => {
      if (a.type === "string") return a.value;
      if (a.description) return a.description;
      return JSON.stringify(a.preview ? a.preview.properties : a);
    });
    console.log(`[console.${msg.params.type}]`, parts.join(" ").slice(0, 500));
  } else if (msg.method === "Runtime.exceptionThrown") {
    const d = msg.params.exceptionDetails;
    console.log("[EXCEPTION]", JSON.stringify(d).slice(0, 800));
  }
};

ws.onopen = async () => {
  try {
    await send("Runtime.enable");
    await send("Log.enable");
    // Force a reload so we can watch the boot sequence live.
    await send("Page.enable");
    await send("Page.reload");
    console.log("--- reloaded, watching console for 10s ---");
  } catch (e) {
    console.error("ERR:", e.message);
  }
};

ws.onerror = () => { console.error("WS error"); process.exit(1); };
setTimeout(() => { console.log("--- done ---"); ws.close(); process.exit(0); }, 12000);
