import { cloudflareTest, readD1Migrations } from "@cloudflare/vitest-plugin";
import { defineConfig } from "vitest/config";

export default defineConfig(async () => {
  const migrations = await readD1Migrations("./migrations");

  return {
    plugins: [
      cloudflareTest({
        wrangler: { configPath: "./wrangler.jsonc" },
        miniflare: {
          bindings: {
            TEST_MIGRATIONS: migrations,
            TELEGRAM_BOT_TOKEN: "123:test-token",
            TELEGRAM_WEBHOOK_SECRET: "test-webhook-secret",
            ALLOWED_CHAT_IDS: "-100111, -100222",
            INBOX_API_KEY: "test-inbox-key",
          },
        },
      }),
    ],
    test: {
      setupFiles: ["./test/apply-migrations.ts"],
    },
  };
});
