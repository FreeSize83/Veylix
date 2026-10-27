// Veylix: обратная связь в Telegram и «Народная карта стратегий».
// Cloudflare Worker. Секреты и привязки (Settings → Variables and Secrets / Bindings):
//   BOT_TOKEN — токен бота от @BotFather (тип Secret)
//   CHAT_ID   — ваш числовой Telegram ID, куда бот пишет (узнать: @userinfobot)
//   DB        — база D1 для народной карты (Bindings → D1 database). Таблицы создаются сами.
// Необязательно: KV-namespace с привязкой RATE — тогда ограничение частоты общее для всех серверов Cloudflare.
// Подробная настройка — tools/FEEDBACK-SETUP.txt.

const MAX_TEXT = 2000;
const MAX_CONTACT = 120;
const MAX_REPORT = 60000;
const PER_HOUR = 5;          // сообщений разработчику в час с одного IP
const KINDS = { idea: "💡 Идея", bug: "🐞 Ошибка", other: "💬 Сообщение" };

// Народная карта
const CROWD_PER_HOUR = 40;   // отчётов в час с одного IP (автоподбор проверяет до ~20 стратегий подряд)
const CROWD_WINDOW = 72;     // часов истории для рейтинга стратегий
const CROWD_RECENT = 3;      // часов для сигнала «массово сломалось»
const CROWD_KEEP = 24 * 14;  // сколько часов хранить данные
const REGION_MIN = 15;       // отчётов по региону, чтобы показывать регион, а не провайдера по всей стране
const STRATEGY_RE = /^[A-Za-z0-9 ()._+\-]{1,40}$/;

const memory = new Map();    // запасной счётчик, если KV не подключён (живёт в одном экземпляре Worker)
let schemaReady = false;

function json(status, body) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "content-type": "application/json; charset=utf-8", "cache-control": "no-store" },
  });
}

function clean(v, max) {
  if (typeof v !== "string") return "";
  // убираем управляющие символы, кроме переводов строк и табуляции
  return v.replace(/[\u0000-\u0008\u000B\u000C\u000E-\u001F\u007F]/g, "").trim().slice(0, max);
}

function nowHour() { return Math.floor(Date.now() / 3600000); }

async function overLimit(env, ip, prefix, limit) {
  const hour = nowHour();
  const key = prefix + ":" + ip + ":" + hour;
  if (env.RATE) {
    const n = parseInt((await env.RATE.get(key)) || "0", 10);
    if (n >= limit) return true;
    await env.RATE.put(key, String(n + 1), { expirationTtl: 3700 });
    return false;
  }
  for (const k of memory.keys()) if (!k.endsWith(":" + hour)) memory.delete(k);
  const n = memory.get(key) || 0;
  if (n >= limit) return true;
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

// ---------------- обратная связь ----------------

async function feedback(request, env) {
  if (!env.BOT_TOKEN || !env.CHAT_ID) return json(500, { ok: false, error: "сервер не настроен" });
  const len = parseInt(request.headers.get("content-length") || "0", 10);
  if (len > 200000) return json(413, { ok: false, error: "слишком большое сообщение" });

  let d;
  try { d = await request.json(); } catch (e) { return json(400, { ok: false, error: "неверный формат" }); }

  const text = clean(d.text, MAX_TEXT);
  if (text.length < 3) return json(400, { ok: false, error: "пустое сообщение" });
  const ip = request.headers.get("cf-connecting-ip") || "?";
  if (await overLimit(env, ip, "r", PER_HOUR)) return json(429, { ok: false, error: "слишком много сообщений, попробуйте через час" });

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
}

// ---------------- народная карта стратегий ----------------
// Хранятся только счётчики по (номер сети провайдера, регион, стратегия, час). IP-адреса не сохраняются:
// провайдера (ASN) и регион определяет сам Cloudflare по входящему соединению.

async function ensureSchema(env) {
  if (schemaReady) return;
  await env.DB.batch([
    env.DB.prepare("CREATE TABLE IF NOT EXISTS crowd (asn INTEGER NOT NULL, region TEXT NOT NULL, strategy TEXT NOT NULL, hour INTEGER NOT NULL, " +
      "n INTEGER NOT NULL DEFAULT 0, d_ok INTEGER NOT NULL DEFAULT 0, y_ok INTEGER NOT NULL DEFAULT 0, both_ok INTEGER NOT NULL DEFAULT 0, " +
      "PRIMARY KEY (asn, region, strategy, hour))"),
    env.DB.prepare("CREATE TABLE IF NOT EXISTS isp (asn INTEGER PRIMARY KEY, name TEXT NOT NULL)"),
  ]);
  schemaReady = true;
}

function network(request) {
  const cf = request.cf || {};
  const asn = typeof cf.asn === "number" ? cf.asn : parseInt(cf.asn || "0", 10);
  return { asn: asn || 0, isp: clean(cf.asOrganization || "", 80), region: clean(cf.region || "", 60), country: clean(cf.country || "", 2) };
}

// Нижняя граница доверительного интервала Уилсона: 9 из 10 честнее, чем 1 из 1
function wilson(ok, n) {
  if (n <= 0) return 0;
  const z = 1.96, p = ok / n, d = 1 + z * z / n;
  return (p + z * z / (2 * n) - z * Math.sqrt(p * (1 - p) / n + z * z / (4 * n * n))) / d;
}

async function report(request, env) {
  if (!env.DB) return json(500, { ok: false, error: "карта не настроена" });
  let d;
  try { d = await request.json(); } catch (e) { return json(400, { ok: false, error: "неверный формат" }); }
  const strategy = clean(d.strategy, 40);
  if (!STRATEGY_RE.test(strategy)) return json(400, { ok: false, error: "неверная стратегия" });
  if (typeof d.discord !== "boolean" || typeof d.youtube !== "boolean") return json(400, { ok: false, error: "нет результатов" });
  const net = network(request);
  if (!net.asn) return json(400, { ok: false, error: "провайдер не определён" });
  const ip = request.headers.get("cf-connecting-ip") || "?";
  if (await overLimit(env, ip, "c", CROWD_PER_HOUR)) return json(429, { ok: false, error: "слишком много отчётов" });

  await ensureSchema(env);
  const hour = nowHour(), dk = d.discord ? 1 : 0, yk = d.youtube ? 1 : 0;
  const stmts = [
    env.DB.prepare("INSERT INTO crowd (asn, region, strategy, hour, n, d_ok, y_ok, both_ok) VALUES (?, ?, ?, ?, 1, ?, ?, ?) " +
      "ON CONFLICT (asn, region, strategy, hour) DO UPDATE SET n = n + 1, d_ok = d_ok + excluded.d_ok, y_ok = y_ok + excluded.y_ok, both_ok = both_ok + excluded.both_ok")
      .bind(net.asn, net.region, strategy, hour, dk, yk, dk & yk),
  ];
  if (net.isp) stmts.push(env.DB.prepare("INSERT INTO isp (asn, name) VALUES (?, ?) ON CONFLICT (asn) DO UPDATE SET name = excluded.name").bind(net.asn, net.isp));
  if (Math.random() < 0.02) stmts.push(env.DB.prepare("DELETE FROM crowd WHERE hour < ?").bind(hour - CROWD_KEEP));
  await env.DB.batch(stmts);
  return json(200, { ok: true });
}

function sums(rows) {
  const t = { n: 0, discord: 0, youtube: 0, both: 0 };
  for (const r of rows) { t.n += r.n; t.discord += r.d_ok; t.youtube += r.y_ok; t.both += r.both_ok; }
  return t;
}

async function map(request, env) {
  if (!env.DB) return json(500, { ok: false, error: "карта не настроена" });
  const net = network(request);
  if (!net.asn) return json(200, { ok: true, asn: 0, isp: "", region: "", scope: "none", strategies: [], recent: sums([]), earlier: sums([]) });
  await ensureSchema(env);
  const hour = nowHour(), from = hour - CROWD_WINDOW;
  const all = (await env.DB.prepare("SELECT region, strategy, hour, n, d_ok, y_ok, both_ok FROM crowd WHERE asn = ? AND hour >= ?")
    .bind(net.asn, from).all()).results || [];
  const inRegion = all.filter(r => r.region === net.region);
  const useRegion = net.region && sums(inRegion).n >= REGION_MIN;
  const rows = useRegion ? inRegion : all;

  const by = new Map();
  for (const r of rows) {
    const s = by.get(r.strategy) || { name: r.strategy, n: 0, discord: 0, youtube: 0, both: 0, last: 0 };
    s.n += r.n; s.discord += r.d_ok; s.youtube += r.y_ok; s.both += r.both_ok; s.last = Math.max(s.last, r.hour);
    by.set(r.strategy, s);
  }
  const strategies = [...by.values()]
    .map(s => ({ ...s, score: Math.round(wilson(s.both, s.n) * 1000) / 1000, hoursAgo: hour - s.last }))
    .sort((a, b) => b.score - a.score || b.n - a.n)
    .slice(0, 30);

  let isp = net.isp;
  if (!isp) {
    const row = await env.DB.prepare("SELECT name FROM isp WHERE asn = ?").bind(net.asn).first();
    if (row) isp = row.name;
  }
  return json(200, {
    ok: true, asn: net.asn, isp, region: useRegion ? net.region : "", country: net.country,
    scope: useRegion ? "region" : "provider", window: CROWD_WINDOW,
    strategies,
    recent: sums(rows.filter(r => r.hour > hour - CROWD_RECENT)),
    earlier: sums(rows.filter(r => r.hour <= hour - CROWD_RECENT)),
  });
}

export default {
  async fetch(request, env) {
    const url = new URL(request.url);
    try {
      if (request.method === "GET" && (url.pathname === "/" || url.pathname === "/health"))
        return json(200, { ok: true, service: "veylix-feedback", map: !!env.DB });
      if (request.method === "POST" && url.pathname === "/feedback") return await feedback(request, env);
      if (request.method === "POST" && url.pathname === "/report") return await report(request, env);
      if (request.method === "GET" && url.pathname === "/map") return await map(request, env);
    } catch (e) {
      return json(500, { ok: false, error: "внутренняя ошибка сервера" });
    }
    return json(404, { ok: false, error: "not found" });
  },
};
