const ws = new WebSocket("ws://127.0.0.1:8765/devtools/page/C7DD9B4993179DED86AD0E77F407AE11");

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
    const expr = `(() => {
      const form = document.querySelector('div[data-testid="login-form"]');
      const username = document.querySelector('input[name="username"]');
      const password = document.querySelector('input[name="password"]');
      const signin = document.querySelector('[data-testid="login-form-sign-in-button"]');
      const error = document.querySelector('[data-testid="login-form-error-tooltip"]');
      return JSON.stringify({
        url: location.href,
        hasLoginForm: !!form,
        loggingIn: form ? form.getAttribute('data-logging-in') : null,
        hasUsername: !!username,
        hasPassword: !!password,
        hasSignIn: !!signin,
        errorText: error ? error.textContent : null,
        bodySnippet: document.body ? document.body.innerText.slice(0, 400) : null
      });
    })()`;
    const r = await send("Runtime.evaluate", { expression: expr, returnByValue: true });
    console.log(r.result.value);
  } catch (e) {
    console.error("ERR:", e.message);
  } finally {
    ws.close();
    process.exit(0);
  }
};

ws.onerror = e => { console.error("WS error"); process.exit(1); };
setTimeout(() => { console.error("timeout"); process.exit(1); }, 15000);
