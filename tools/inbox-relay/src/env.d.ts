declare namespace Cloudflare {
  interface Env {
    DB: D1Database;
    /** Bot token from BotFather; used only to set the 👀 reaction. */
    TELEGRAM_BOT_TOKEN: string;
    /** Value passed as `secret_token` to setWebhook; Telegram echoes it in a header. */
    TELEGRAM_WEBHOOK_SECRET: string;
    /** Comma-separated chat ids whose messages are stored; everything else is dropped. */
    ALLOWED_CHAT_IDS: string;
    /** Bearer key Roamint uses for GET /inbox and POST /inbox/ack. */
    INBOX_API_KEY: string;
  }
}
