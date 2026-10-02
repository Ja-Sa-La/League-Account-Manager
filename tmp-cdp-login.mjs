const http = await import("node:http");

const USERNAME = process.env.RIOT_USER || "";
const PASSWORD = process.env.RIOT_PASS || "";

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
    // 1. Fill both fields with React-compatible native setter + input events
    const fill = await send("Runtime.evaluate", {
      expression: `(() => {
        const setter = Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, 'value').set;
        const fill = (sel, val) => {
          const el = document.querySelector(sel);
          if (!el) return false;
          el.focus();
          setter.call(el, val);
          el.dispatchEvent(new Event('input', { bubbles: true }));
          el.dispatchEvent(new Event('change', { bubbles: true }));
          el.blur();
          return el.value === val;
        };
        const u = fill('input[name="username"]', ${JSON.stringify(USERNAME)});
        const p = fill('input[name="password"]', ${JSON.stringify(PASSWORD)});
        return JSON.stringify({ usernameFilled: u, passwordFilled: p });
      })()`,
      returnByValue: true
    });
    console.log("fill:", fill.result.value);

    // 2. Click sign in (form onSubmit -> client runs its own Tabasco + POST)
    const click = await send("Runtime.evaluate", {
      expression: `(() => {
        const btn = document.querySelector('[data-testid="login-form-sign-in-button"]');
        if (!btn) return 'no button';
        btn.click();
        return 'clicked';
      })()`,
      returnByValue: true
    });
    console.log("click:", click.result.value);

    // 3. Poll the form state for up to 15s
    for (let i = 0; i < 15; i++) {
      await new Promise(r => setTimeout(r, 1000));
      const st = await send("Runtime.evaluate", {
        expression: `(() => {
          const form = document.querySelector('div[data-testid="login-form"]');
          const err = document.querySelector('[data-testid="login-form-error-tooltip"]');
          return JSON.stringify({
            loggingIn: form ? form.getAttribute('data-logging-in') : null,
            error: err ? err.textContent.trim() : null
          });
        })()`,
        returnByValue: true
      });
      const s = JSON.parse(st.result.value);
      console.log(`t+${i + 1}s:`, JSON.stringify(s));
      if (s.error || s.loggingIn === "false") break;
      if (s.loggingIn === "true" && i > 10) break;
    }
  } catch (e) {
    console.error("ERR:", e.message);
  } finally {
    ws.close();
    process.exit(0);
  }
};

ws.onerror = () => { console.error("WS error"); process.exit(1); };
setTimeout(() => { console.error("timeout"); process.exit(1); }, 30000);
