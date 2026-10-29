// Roamint inbox relay (docs/features/0024): an always-on mailbox between Telegram and the local app.
// Telegram pushes updates here by webhook; Roamint pulls them when it runs and acks (deletes) them.
// No parsing, no business logic — the relay only stores envelopes and marks messages as seen.

type Env = Cloudflare.Env;

const MAX_BATCH = 100;

interface TelegramUpdate {
  message?: {
    message_id: number;
    chat: { id: number };
    text?: string;
  };
}

export default {
  async fetch(request, env, ctx): Promise<Response> {
    const { pathname } = new URL(request.url);

    if (request.method === "POST" && pathname === "/telegram") {
      return handleTelegram(request, env, ctx);
    }
    if (request.method === "GET" && pathname === "/inbox") {
      return authorized(request, env) ? listInbox(request, env) : unauthorized();
    }
    if (request.method === "POST" && pathname === "/inbox/ack") {
      return authorized(request, env) ? ackInbox(request, env) : unauthorized();
    }
    return new Response("Not found", { status: 404 });
  },
} satisfies ExportedHandler<Env>;

async function handleTelegram(request: Request, env: Env, ctx: ExecutionContext): Promise<Response> {
  const secret = request.headers.get("X-Telegram-Bot-Api-Secret-Token") ?? "";
  if (!safeEqual(secret, env.TELEGRAM_WEBHOOK_SECRET)) {
    return unauthorized();
  }

  const body = await request.text();
  let update: TelegramUpdate;
  try {
    update = JSON.parse(body) as TelegramUpdate;
  } catch {
    // Malformed payloads are acknowledged, not retried: Telegram would redeliver the same bytes forever.
    return ok();
  }

  // Only new text messages from allowlisted chats are stored. Edits, photos, joins and foreign chats
  // get a 200 so Telegram doesn't retry them.
  const message = update.message;
  if (!message?.text || !allowedChats(env).has(String(message.chat.id))) {
    return ok();
  }

  const result = await env.DB
    .prepare(
      `INSERT INTO inbox (source, external_id, received_at, payload)
       VALUES ('telegram', ?, ?, ?)
       ON CONFLICT (source, external_id) DO NOTHING`,
    )
    .bind(`${message.chat.id}:${message.message_id}`, new Date().toISOString(), body)
    .run();

  // React only on first store, so a webhook retry doesn't re-react. Best-effort: a failed reaction
  // must never fail the webhook (that would make Telegram redeliver an already-stored message).
  if (result.meta.changes > 0) {
    ctx.waitUntil(react(env, message.chat.id, message.message_id).catch(() => undefined));
  }
  return ok();
}

async function listInbox(request: Request, env: Env): Promise<Response> {
  const requested = Number(new URL(request.url).searchParams.get("limit") ?? MAX_BATCH);
  const limit = Number.isInteger(requested) && requested > 0 ? Math.min(requested, MAX_BATCH) : MAX_BATCH;

  const { results } = await env.DB
    .prepare("SELECT id, source, external_id, received_at, payload FROM inbox ORDER BY id LIMIT ?")
    .bind(limit)
    .all<{ id: number; source: string; external_id: string; received_at: string; payload: string }>();

  const items = results.map((row) => ({
    id: row.id,
    source: row.source,
    externalId: row.external_id,
    receivedAt: row.received_at,
    payload: row.payload,
  }));
  return Response.json({ items });
}

async function ackInbox(request: Request, env: Env): Promise<Response> {
  let ids: unknown;
  try {
    ({ ids } = (await request.json()) as { ids?: unknown });
  } catch {
    return badRequest();
  }
  if (!Array.isArray(ids) || ids.length > MAX_BATCH || !ids.every((id) => Number.isInteger(id))) {
    return badRequest();
  }
  if (ids.length === 0) {
    return Response.json({ deleted: 0 });
  }

  const placeholders = ids.map(() => "?").join(", ");
  const result = await env.DB
    .prepare(`DELETE FROM inbox WHERE id IN (${placeholders})`)
    .bind(...ids)
    .run();
  return Response.json({ deleted: result.meta.changes });
}

async function react(env: Env, chatId: number, messageId: number): Promise<void> {
  await fetch(`https://api.telegram.org/bot${env.TELEGRAM_BOT_TOKEN}/setMessageReaction`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({
      chat_id: chatId,
      message_id: messageId,
      reaction: [{ type: "emoji", emoji: "👀" }],
    }),
  });
}

function authorized(request: Request, env: Env): boolean {
  const header = request.headers.get("Authorization") ?? "";
  return header.startsWith("Bearer ") && safeEqual(header.slice("Bearer ".length), env.INBOX_API_KEY);
}

function allowedChats(env: Env): Set<string> {
  return new Set((env.ALLOWED_CHAT_IDS ?? "").split(",").map((id) => id.trim()).filter(Boolean));
}

// Constant-time comparison; an unset secret never matches (guards a deploy that forgot `secret put`).
function safeEqual(actual: string, expected: string | undefined): boolean {
  if (!expected) {
    return false;
  }
  const encoder = new TextEncoder();
  const a = encoder.encode(actual);
  const b = encoder.encode(expected);
  return a.byteLength === b.byteLength && crypto.subtle.timingSafeEqual(a, b);
}

const ok = () => new Response("OK");
const unauthorized = () => new Response("Unauthorized", { status: 401 });
const badRequest = () => new Response("Bad request", { status: 400 });
