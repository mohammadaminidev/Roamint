import { applyD1Migrations } from "cloudflare:test";
import { env } from "cloudflare:workers";

// Storage is isolated per test file, so the schema is applied before each run.
await applyD1Migrations(env.DB, env.TEST_MIGRATIONS);
