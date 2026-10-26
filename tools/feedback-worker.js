// Veylix → Telegram: приём отзывов из приложения.
// Cloudflare Worker. Секреты (Settings → Variables and Secrets):
//   BOT_TOKEN — токен бота от @BotFather (тип Secret)
//   CHAT_ID   — ваш числовой Telegram ID, куда бот пишет (узнать: @userinfobot)
// Необязательно: KV-namespace с привязкой RATE — тогда ограничение частоты общее для всех серверов Cloudflare.
// Подробная настройка — tools/FEEDBACK-SETUP.txt.

const MAX_TEXT = 2000;
const MAX_CONTACT = 120;
const MAX_REPORT = 60000;
const PER_HOUR = 5;          // сообщений в час с одного IP
const KINDS = { idea: "💡 Идея", bug: "🐞 Ошибка", other: "💬 Сообщение" };

const memory = new Map();    // запасной счётчик, если KV не подключён (живёт в одном экземпляре Worker)

function json(status, body) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "content-type": "application/json; charset=utf-8" },
  });
}

function clean(v, max) {
  if (typeof v !== "string") return "";
  // убираем управляющие символы, кроме переводов строк и табуляции
  return v.replace(/[\u0000-\u0008\u000B\u000C\u000E-\u001F\u007F]/g, "").trim().slice(0, max);
}

async function overLimit(env, ip) {
  const hour = Math.floor(Date.now() / 3600000);
  const key = "r:" + ip + ":" + hour;
  if (env.RATE) {
    const n = parseInt((await env.RATE.get(key)) || "0", 10);
    if (n >= PER_HOUR) return true;
    await env.RATE.put(key, String(n + 1), { expirationTtl: 3700 });
    return false;
  }
  for (const k of memory.keys()) if (!k.endsWith(":" + hour)) memory.delete(k);
  const n = memory.get(key) || 0;
  if (n >= PER_HOUR) return true;
  memory.set(key, n + 1);
  return false;
}

async function telegram(env, method, body) {
  const r = await fetch("https://api.telegram.org/bot" + env.BOT_TOKEN + "/" + method, { method: "POST", body });
  if (!r.ok) {
    let desc = "";
    try { desc = (await r.json()).description || ""; } catch (e) { }
    throw new Error("Telegram " + r.status + (desc ? ": " + desc.slice(0, 150) : ""));
  }
}

export default {
  async fetch(request, env) {
    const url = new URL(request.url);
    if (request.method === "GET" && (url.pathname === "/" || url.pathname === "/health"))
      return json(200, { ok: true, service: "veylix-feedback" });
    if (request.method !== "POST" || url.pathname !== "/feedback") return json(404, { ok: false, error: "not found" });
    if (!env.BOT_TOKEN || !env.CHAT_ID) return json(500, { ok: false, error: "сервер не настроен" });

    const len = parseInt(request.headers.get("content-length") || "0", 10);
    if (len > 200000) return json(413, { ok: false, error: "слишком большое сообщение" });

    let d;
    try { d = await request.json(); } catch (e) { return json(400, { ok: false, error: "неверный формат" }); }

    const text = clean(d.text, MAX_TEXT);
    if (text.length < 3) return json(400, { ok: false, error: "пустое сообщение" });
    const ip = request.headers.get("cf-connecting-ip") || "?";
    if (await overLimit(env, ip)) return json(429, { ok: false, error: "слишком много сообщений, попробуйте через час" });

    const kind = KINDS[d.kind] || KINDS.other;
    const contact = clean(d.contact, MAX_CONTACT);
    const meta = [
      "Veylix " + clean(d.version, 20),
      clean(d.os, 80),
      d.strategy ? "стратегия " + clean(d.strategy, 60) + (d.running ? " (вкл)" : " (выкл)") : "",
      request.cf && request.cf.country ? "страна " + request.cf.country : "",
    ].filter(Boolean).join(" · ");

    let msg = kind + "\n\n" + text + "\n\n" + (contact ? "✉️ " + contact + "\n" : "") + meta;
    if (msg.length > 4000) msg = msg.slice(0, 3990) + "…";

    try {
      await telegram(env, "sendMessage", new URLSearchParams({ chat_id: env.CHAT_ID, text: msg, disable_web_page_preview: "true" }));
      const report = clean(d.report, MAX_REPORT);
      if (report.length > 0) {
        const form = new FormData();
        form.append("chat_id", env.CHAT_ID);
        form.append("caption", "Отчёт к сообщению выше");
        form.append("document", new Blob([report], { type: "text/plain" }), "veylix-report-" + Date.now() + ".txt");
        await telegram(env, "sendDocument", form);
      }
    } catch (e) {
      // описание ошибки Telegram (без токена) — чтобы автор сразу видел причину: 401 — неверный токен, 403/400 — не нажат Start или неверный CHAT_ID
      return json(502, { ok: false, error: "не удалось доставить сообщение, попробуйте позже", detail: String(e.message || e).slice(0, 200) });
    }
    return json(200, { ok: true });
  },
};
