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
for (const t of list) {
  console.log(t.type, "|", t.title, "|", t.url, "|", t.webSocketDebuggerUrl);
}
