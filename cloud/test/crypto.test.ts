import assert from "node:assert/strict";
import test from "node:test";
import { hashPassword, PBKDF2_ITERATIONS, signJwt, verifyJwt, verifyPassword } from "../src/crypto.ts";
import { isOlder, normalizeTime, parseTweaks } from "../src/validate.ts";

test("PBKDF2 uses 100000 iterations and verifies", async () => {
  assert.equal(PBKDF2_ITERATIONS, 100_000);
  const stored = await hashPassword("phrase-secrete-1", 1000);
  assert.equal(stored.iterations, 1000);
  assert.equal(stored.algo, "PBKDF2-SHA256");
  assert.equal(await verifyPassword("phrase-secrete-1", stored.hash, stored.salt, stored.iterations), true);
  assert.equal(await verifyPassword("autre-mot", stored.hash, stored.salt, stored.iterations), false);
});

test("JWT roundtrip rejects a bad signature", async () => {
  const token = await signJwt({
    sub: "11111111-1111-4111-8111-111111111111",
    sid: "22222222-2222-4222-8222-222222222222",
    username: "ada",
    iat: 1_700_000_000,
    exp: 4_000_000_000,
  }, "secret-assez-long");
  const payload = await verifyJwt(token, "secret-assez-long");
  assert.equal(payload?.username, "ada");
  assert.equal(await verifyJwt(token, "autre-secret-long"), null);
  const [h, b] = token.split(".");
  assert.equal(await verifyJwt(`${h}.${b}.aaaa`, "secret-assez-long"), null);
});

test("timestamps clamp the future and detect older writes", () => {
  const now = Date.parse("2026-10-24T12:00:00.000Z");
  assert.equal(normalizeTime("2026-10-24T12:00:00.000Z", now), "2026-10-24T12:00:00.000Z");
  assert.equal(normalizeTime("2026-10-24T12:10:00.000Z", now), "2026-10-24T12:00:00.000Z");
  assert.equal(normalizeTime("pas une date", now), null);
  assert.equal(isOlder("2026-10-24T11:00:00.000Z", "2026-10-24T12:00:00.000Z"), true);
  assert.equal(isOlder("2026-10-24T12:00:00.000Z", "2026-10-24T12:00:00.000Z"), false);
});

test("preset tweaks reject unknown fields", () => {
  const ok = parseTweaks({ powerPlan: "ultimate", gameMode: "enable", cleanTemp: true });
  assert.equal(ok.ok, true);
  const bad = parseTweaks({ powerPlan: "ultimate", inject: true });
  assert.equal(bad.ok, false);
});
