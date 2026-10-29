import { createExecutionContext, waitOnExecutionContext } from "cloudflare:test";
import { env } from "cloudflare:workers";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import worker from "../src/index";

const ALLOWED_CHAT = -100111;
const BASE = "https://relay.test";

let telegramCalls: { url: string; body: unknown }[];

beforeEach(async () => {
  await env.DB.exec("DELETE FROM inbox");
  telegramCalls = [];
  vi.spyOn(globalThis, "fetch").mockImplementation(async (input, init) => {
    telegramCalls.push({ url: String(input), body: JSON.parse(String(init?.body)) });
    return Response.json({ ok: true });
  });
});

afterEach(() => vi.restoreAllMocks());

async function call(request: Request): Promise<Response> {
  const ctx = createExecutionContext();
  const response = await worker.fetch(request as Request<unknown, IncomingRequestCfProperties>, env, ctx);
  await waitOnExecutionContext(ctx);
  return response;
}

function webhook(update: unknown, secret: string | null = "test-webhook-secret"): Request {
  const headers: Record<string, string> = { "Content-Type": "application/json" };
  if (secret !== null) {
    headers["X-Telegram-Bot-Api-Secret-Token"] = secret;
  }
  const body = typeof update === "string" ? update : JSON.stringify(update);
  return new Request(`${BASE}/telegram`, { method: "POST", headers, body });
}

function textUpdate(messageId: number, chatId = ALLOWED_CHAT, text = "15 هزار درام شام") {
  return { update_id: 1000 + messageId, message: { message_id: messageId, chat: { id: chatId }, text } };
}

function pull(query = "", key = "test-inbox-key"): Request {
  return new Request(`${BASE}/inbox${query}`, { headers: { Authorization: `Bearer ${key}` } });
}

function ack(body: unknown, key = "test-inbox-key"): Request {
  return new Request(`${BASE}/inbox/ack`, {
    method: "POST",
    headers: { Authorization: `Bearer ${key}`, "Content-Type": "application/json" },
    body: typeof body === "string" ? body : JSON.stringify(body),
  });
}

async function storedCount(): Promise<number> {
  return (await env.DB.prepare("SELECT COUNT(*) AS n FROM inbox").first<number>("n")) ?? 0;
}

describe("POST /telegram", () => {
  it("rejects a wrong or missing webhook secret without storing", async () => {
    expect((await call(webhook(textUpdate(1), "wrong"))).status).toBe(401);
    expect((await call(webhook(textUpdate(1), null))).status).toBe(401);
    expect(await storedCount()).toBe(0);
  });

  it("stores an allowlisted text message as a telegram envelope and reacts 👀", async () => {
    const update = textUpdate(42);

    const response = await call(webhook(update));

    expect(response.status).toBe(200);
    const row = await env.DB.prepare("SELECT source, external_id, payload FROM inbox").first();
    expect(row).toMatchObject({ source: "telegram", external_id: `${ALLOWED_CHAT}:42` });
    expect(JSON.parse(String(row?.payload))).toEqual(update);
    expect(telegramCalls).toHaveLength(1);
    expect(telegramCalls[0]?.url).toContain("/bot123:test-token/setMessageReaction");
    expect(telegramCalls[0]?.body).toEqual({
      chat_id: ALLOWED_CHAT,
      message_id: 42,
      reaction: [{ type: "emoji", emoji: "👀" }],
    });
  });

  it("treats a redelivered update as a no-op: one row, one reaction", async () => {
    await call(webhook(textUpdate(7)));
    const second = await call(webhook(textUpdate(7)));

    expect(second.status).toBe(200);
    expect(await storedCount()).toBe(1);
    expect(telegramCalls).toHaveLength(1);
  });

  it("accepts any chat in the comma-separated allowlist", async () => {
    await call(webhook(textUpdate(1, -100222)));
    expect(await storedCount()).toBe(1);
  });

  it.each([
    ["a foreign chat", textUpdate(1, -100999)],
    ["a non-text message", { update_id: 1, message: { message_id: 1, chat: { id: ALLOWED_CHAT }, photo: [] } }],
    ["an edited message", { update_id: 1, edited_message: { message_id: 1, chat: { id: ALLOWED_CHAT }, text: "x" } }],
    ["malformed JSON", "{not json"],
  ])("acknowledges %s with 200 but stores nothing", async (_, update) => {
    const response = await call(webhook(update));

    expect(response.status).toBe(200);
    expect(await storedCount()).toBe(0);
    expect(telegramCalls).toHaveLength(0);
  });

  it("still stores and returns 200 when the reaction call fails", async () => {
    vi.mocked(globalThis.fetch).mockRejectedValue(new Error("telegram down"));

    const response = await call(webhook(textUpdate(5)));

    expect(response.status).toBe(200);
    expect(await storedCount()).toBe(1);
  });
});

describe("GET /inbox", () => {
  it("requires the bearer key", async () => {
    expect((await call(pull("", "wrong"))).status).toBe(401);
    expect((await call(new Request(`${BASE}/inbox`))).status).toBe(401);
  });

  it("returns envelopes oldest first, capped by limit", async () => {
    for (const id of [1, 2, 3]) {
      await call(webhook(textUpdate(id)));
    }

    const all = (await (await call(pull())).json()) as { items: { externalId: string; source: string }[] };
    const firstTwo = (await (await call(pull("?limit=2"))).json()) as { items: unknown[] };

    expect(all.items.map((i) => i.externalId)).toEqual([1, 2, 3].map((id) => `${ALLOWED_CHAT}:${id}`));
    expect(all.items[0]).toMatchObject({ source: "telegram" });
    expect(firstTwo.items).toHaveLength(2);
  });
});

describe("POST /inbox/ack", () => {
  it("requires the bearer key", async () => {
    expect((await call(ack({ ids: [1] }, "wrong"))).status).toBe(401);
  });

  it("deletes acked envelopes so the next pull excludes them", async () => {
    for (const id of [1, 2, 3]) {
      await call(webhook(textUpdate(id)));
    }
    const { items } = (await (await call(pull())).json()) as { items: { id: number }[] };

    const response = await call(ack({ ids: [items[0]?.id, items[1]?.id] }));

    expect(await response.json()).toEqual({ deleted: 2 });
    const after = (await (await call(pull())).json()) as { items: { externalId: string }[] };
    expect(after.items.map((i) => i.externalId)).toEqual([`${ALLOWED_CHAT}:3`]);
  });

  it.each([["not JSON", "{oops"], ["ids missing", {}], ["non-integer ids", { ids: ["1"] }]])(
    "rejects %s with 400",
    async (_, body) => {
      expect((await call(ack(body))).status).toBe(400);
    },
  );
});

it("returns 404 for unknown routes", async () => {
  expect((await call(new Request(`${BASE}/`))).status).toBe(404);
});
