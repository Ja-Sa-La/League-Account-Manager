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
  }
};

ws.onopen = async () => {
  try {
    await send("DOM.enable");
    await send("Runtime.enable");
    const doc = await send("DOM.getDocument", { depth: 4 });
    const outer = await send("Runtime.evaluate", {
      expression: `document.documentElement.outerHTML.length + ' chars; body children: ' + document.body.children.length + ' [' + [...document.body.children].map(c=>c.tagName+'#'+(c.id||'')+'.'+(c.className||'')).join(', ') + ']';`,
      returnByValue: true
    });
    console.log("html:", outer.result.value);
    const root = await send("Runtime.evaluate", {
      expression: `document.getElementById('root') ? document.getElementById('root').outerHTML.slice(0,500) : 'no #root; html head length: ' + document.head.outerHTML.length`,
      returnByValue: true
    });
    console.log("root:", root.result.value);
  } catch (e) {
    console.error("ERR:", e.message);
  } finally {
    ws.close();
    process.exit(0);
  }
};

ws.onerror = () => { console.error("WS error"); process.exit(1); };
setTimeout(() => { console.error("timeout"); process.exit(1); }, 8000);
