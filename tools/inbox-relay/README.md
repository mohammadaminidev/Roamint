# Roamint inbox relay

An always-on mailbox for messages sent while Roamint (which runs locally) is off. Design and rationale:
[docs/features/0024-telegram-capture.md](../../docs/features/0024-telegram-capture.md).

```
Telegram ──webhook──▶ Worker ──▶ D1 `inbox` table ◀── GET /inbox, POST /inbox/ack ── Roamint
                        └─ reacts 👀 on the message once it is stored
```

| Route | Auth | Purpose |
|---|---|---|
| `POST /telegram` | `X-Telegram-Bot-Api-Secret-Token` header | Webhook. Stores new text messages from allowlisted chats; everything else gets 200 and is dropped. |
| `GET /inbox?limit=N` | `Authorization: Bearer <INBOX_API_KEY>` | Oldest-first envelopes (`id`, `source`, `externalId`, `receivedAt`, raw `payload`), max 100. |
| `POST /inbox/ack` | same | Body `{ "ids": [1, 2] }`; deletes those rows. |

## Develop

```powershell
npm install
npm test            # runs inside the Workers runtime against a local D1
npm run typecheck
```

`npm run dev` serves it locally; copy `.dev.vars.example` to `.dev.vars` and run `npm run migrate:local` first.

## First-time setup

All commands run from `tools/inbox-relay`. Values in `<angle brackets>` are yours. Never commit them.

### 1. Cloudflare

You need a free Cloudflare account.

```powershell
npm install
npx wrangler login
npx wrangler d1 create roamint-inbox
```

Copy the printed `database_id` into `wrangler.jsonc`. It's an identifier, not a credential. Then create the table:

```powershell
npm run migrate:remote
```

### 2. Telegram bot

1. In Telegram, open **@BotFather** → `/newbot` and pick a name. Keep the token it gives you secret.
2. In BotFather, send `/setprivacy`, choose your bot, then **Disable**. Do this **before** adding the bot to the group, because the setting only applies when the bot joins.
3. Create a group with you and your partner, and add the bot.
4. Send any message in the group. Then read the group's chat id. This only works before the webhook is set:

   ```powershell
   curl.exe "https://api.telegram.org/bot<BOT_TOKEN>/getUpdates"
   ```

   Look for `"chat":{"id":-100…`. That negative number is the chat id.

### 3. Secrets

Generate two random values, one for the webhook secret and one for the inbox key:

```powershell
[Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
```

Store all four secrets on the Worker. Each command prompts for the value:

```powershell
npx wrangler secret put TELEGRAM_BOT_TOKEN
npx wrangler secret put TELEGRAM_WEBHOOK_SECRET
npx wrangler secret put ALLOWED_CHAT_IDS      # e.g. -1001234567890 (comma-separate several)
npx wrangler secret put INBOX_API_KEY
```

Roamint needs `INBOX_API_KEY` too. Once the Worker is deployed (step 4), store it with the Worker URL in
Roamint.Web's user-secrets, from the repo root:

```powershell
dotnet user-secrets set "InboxRelay:BaseUrl" "https://roamint-inbox-relay.<subdomain>.workers.dev" --project src/Bootstrap/Roamint.Web
dotnet user-secrets set "InboxRelay:ApiKey" "<INBOX_API_KEY>" --project src/Bootstrap/Roamint.Web
```

### 4. Deploy and connect the webhook

```powershell
npm run deploy
```

This prints the Worker URL, `https://roamint-inbox-relay.<subdomain>.workers.dev`. Point Telegram at it:

```powershell
Invoke-RestMethod -Method Post -Uri "https://api.telegram.org/bot<BOT_TOKEN>/setWebhook" -Body @{
  url             = "https://roamint-inbox-relay.<subdomain>.workers.dev/telegram"
  secret_token    = "<TELEGRAM_WEBHOOK_SECRET>"
  allowed_updates = '["message"]'
}

curl.exe "https://api.telegram.org/bot<BOT_TOKEN>/getWebhookInfo"
```

`getWebhookInfo` should show your URL and no `last_error_message`.

### 5. Smoke test

1. Write a message in the group. Within a second the bot should react with 👀.
2. Pull it:

   ```powershell
   curl.exe -H "Authorization: Bearer <INBOX_API_KEY>" "https://roamint-inbox-relay.<subdomain>.workers.dev/inbox"
   ```

Until Roamint's puller exists (0024 step 5), messages simply wait in D1. To clear test messages, ack their ids
(use the `id` values from the pull). The response should be `deleted: 1`. Ack only deletes the row; the 👀
reaction stays, since only Roamint changes it later:

```powershell
Invoke-RestMethod -Method Post -Uri "https://roamint-inbox-relay.<subdomain>.workers.dev/inbox/ack" `
  -Headers @{ Authorization = "Bearer <INBOX_API_KEY>" } -ContentType "application/json" -Body '{"ids":[1]}'
```

(Avoid `curl.exe -d '{\"ids\":[1]}'`: PowerShell 7 passes the backslashes through literally, so the relay
gets invalid JSON and answers `Bad request`.)

## Connect Roamint

Roamint pulls from the relay on startup and every minute while it runs. It needs four groups of settings in
Roamint.Web's user-secrets. Run these from the repo root, and set them **before** the first start (see the note below).

**1. Relay.** `InboxRelay:BaseUrl` and `InboxRelay:ApiKey`, from step 3 above.

**2. Household members.** Your Telegram user ids appear as `"from":{"id":…}` in the `getUpdates` output from step 2,
or in a pulled payload. The key is what gets stored on each expense, so pick it once and never rename it.

```powershell
dotnet user-secrets set "Household:Members:0:Key" "<you>" --project src/Bootstrap/Roamint.Web
dotnet user-secrets set "Household:Members:0:DisplayName" "<Your name>" --project src/Bootstrap/Roamint.Web
dotnet user-secrets set "Household:Members:0:TelegramUserId" "<your telegram id>" --project src/Bootstrap/Roamint.Web
dotnet user-secrets set "Household:Members:1:Key" "<partner>" --project src/Bootstrap/Roamint.Web
dotnet user-secrets set "Household:Members:1:DisplayName" "<Partner name>" --project src/Bootstrap/Roamint.Web
dotnet user-secrets set "Household:Members:1:TelegramUserId" "<partner telegram id>" --project src/Bootstrap/Roamint.Web
```

**3. Bot token.** This lets Roamint set 👍 / ✍ / 🤷 and reply:

```powershell
dotnet user-secrets set "Telegram:BotToken" "<BOT_TOKEN>" --project src/Bootstrap/Roamint.Web
```

**4. Gemini key.** `Ocr:ApiKey` is already set for receipts. It also reads the chat messages.

With no household members configured, Roamint stores pulled messages but leaves them untouched, so nothing is
wrongly ignored. They're processed on the first start after the members are set.

What happens to each message:

| Reaction | Meaning |
|---|---|
| 👀 | Stored by the relay; Roamint hasn't processed it yet |
| 👍 + reply | Recorded automatically. This happens when the item (e.g. "شام") was confirmed before, the currency was stated, and there's no same-day twin |
| ✍ | Waiting in the app's review bar. Confirm it there and the reaction flips to 👍, and that item is learned for next time |
| 🤷 | Couldn't be read (e.g. Gemini unavailable). Retried on the next start |
| *(none)* | Not an expense, or not from a household member |

## Troubleshooting

- **No 👀 reaction.** Run `getWebhookInfo` and check `last_error_message`. Also check that the chat id in `ALLOWED_CHAT_IDS` is exact, including the `-100` prefix, and that the group allows reactions.
- **The bot sees nothing in the group.** Privacy mode is still on. Disable it in BotFather, then remove the bot from the group and add it again.
- **`getUpdates` returns an error about a webhook.** That's expected once the webhook is set. Use `deleteWebhook` only if you need `getUpdates` again, for example to find a new chat id.
- **Logs:** `npx wrangler tail` streams live Worker logs.
