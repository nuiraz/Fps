import { dummyVerify, hashPassword, PBKDF2_ITERATIONS, signJwt, verifyJwt, verifyPassword } from "./crypto";
import { bearer, clientIp, error, json, log, readJson } from "./http";
import { assertLoginAllowed, recordAttempt } from "./rateLimit";
import { cleanPassword, cleanUsername } from "./validate";

type UserRow = {
  id: string;
  username: string;
  password_hash: string;
  password_salt: string;
  password_iterations: number;
  created_at: string;
  last_login_at: string | null;
};

export type AuthUser = { id: string; username: string };

export async function register(request: Request, env: Env): Promise<Response> {
  const body = await readJson(request);
  if (!body.ok) return body.response;
  const parsed = authBody(body.value);
  if (!parsed.ok) return error(parsed.error, 400);
  const existing = await env.DB.prepare("SELECT id FROM users WHERE username = ?").bind(parsed.username).first();
  if (existing) return error("Ce nom d'utilisateur est déjà pris.", 409);
  const password = await hashPassword(parsed.password, PBKDF2_ITERATIONS);
  const id = crypto.randomUUID();
  const createdAt = new Date().toISOString();
  await env.DB.prepare(
    `INSERT INTO users (id, username, password_hash, password_salt, password_iterations, algo, created_at, last_login_at)
     VALUES (?, ?, ?, ?, ?, ?, ?, ?)`,
  ).bind(id, parsed.username, password.hash, password.salt, password.iterations, password.algo, createdAt, createdAt).run();
  log("register", { userId: id });
  return openSession(env, { id, username: parsed.username, created_at: createdAt, last_login_at: createdAt }, parsed.remember);
}

export async function login(request: Request, env: Env): Promise<Response> {
  const body = await readJson(request);
  if (!body.ok) return body.response;
  const parsed = authBody(body.value);
  if (!parsed.ok) return error(parsed.error, 400);
  const ip = clientIp(request);
  const limited = await assertLoginAllowed(env.DB, ip, parsed.username);
  if (limited) return limited;
  const user = await env.DB.prepare(
    "SELECT id, username, password_hash, password_salt, password_iterations, created_at, last_login_at FROM users WHERE username = ?",
  ).bind(parsed.username).first<UserRow>();
  if (!user) {
    await dummyVerify(parsed.password);
    await recordAttempt(env.DB, ip, parsed.username, false);
    log("login_failed", { reason: "unknown" });
    return error("Identifiants invalides.", 401);
  }
  const matches = await verifyPassword(parsed.password, user.password_hash, user.password_salt, user.password_iterations);
  if (!matches) {
    await recordAttempt(env.DB, ip, parsed.username, false);
    log("login_failed", { reason: "password" });
    return error("Identifiants invalides.", 401);
  }
  await recordAttempt(env.DB, ip, parsed.username, true);
  const now = new Date().toISOString();
  await env.DB.prepare("UPDATE users SET last_login_at = ? WHERE id = ?").bind(now, user.id).run();
  await env.DB.prepare("DELETE FROM sessions WHERE expires_at < ?").bind(now).run();
  log("login", { userId: user.id });
  return openSession(env, { ...user, last_login_at: now }, parsed.remember);
}

export async function logout(request: Request, env: Env): Promise<Response> {
  const user = await requireUser(request, env);
  if (user instanceof Response) return user;
  await env.DB.prepare("DELETE FROM sessions WHERE id = ? AND user_id = ?").bind(user.sessionId, user.id).run();
  log("logout", { userId: user.id });
  return json({ ok: true });
}

export async function me(request: Request, env: Env): Promise<Response> {
  const user = await requireUser(request, env);
  if (user instanceof Response) return user;
  const row = await env.DB.prepare(
    "SELECT id, username, created_at, last_login_at FROM users WHERE id = ?",
  ).bind(user.id).first<{ id: string; username: string; created_at: string; last_login_at: string | null }>();
  if (!row) return error("Session expirée ou jeton invalide.", 401);
  return json({ id: row.id, username: row.username, createdAt: row.created_at, lastLoginAt: row.last_login_at });
}

export async function requireUser(request: Request, env: Env): Promise<{ id: string; username: string; sessionId: string } | Response> {
  const token = bearer(request);
  if (!token) return error("Session expirée ou jeton invalide.", 401);
  if (!env.JWT_SECRET || env.JWT_SECRET.length < 16) {
    log("missing_secret");
    return error("Erreur interne.", 500);
  }
  const payload = await verifyJwt(token, env.JWT_SECRET);
  if (!payload) return error("Session expirée ou jeton invalide.", 401);
  const session = await env.DB.prepare(
    "SELECT id, user_id, expires_at FROM sessions WHERE id = ?",
  ).bind(payload.sid).first<{ id: string; user_id: string; expires_at: string }>();
  if (!session || session.user_id !== payload.sub || Date.parse(session.expires_at) <= Date.now()) {
    return error("Session expirée ou jeton invalide.", 401);
  }
  return { id: payload.sub, username: payload.username, sessionId: payload.sid };
}

async function openSession(env: Env, user: { id: string; username: string; created_at: string; last_login_at: string | null }, remember: boolean): Promise<Response> {
  if (!env.JWT_SECRET || env.JWT_SECRET.length < 16) {
    log("missing_secret");
    return error("Erreur interne.", 500);
  }
  const now = Date.now();
  const ttl = (remember ? 30 * 24 : 12) * 60 * 60 * 1000;
  const sessionId = crypto.randomUUID();
  const expiresAt = new Date(now + ttl).toISOString();
  await env.DB.prepare(
    "INSERT INTO sessions (id, user_id, expires_at, created_at) VALUES (?, ?, ?, ?)",
  ).bind(sessionId, user.id, expiresAt, new Date(now).toISOString()).run();
  const token = await signJwt({
    sub: user.id,
    sid: sessionId,
    username: user.username,
    iat: Math.floor(now / 1000),
    exp: Math.floor((now + ttl) / 1000),
  }, env.JWT_SECRET);
  return json({
    token,
    expiresAt,
    user: { id: user.id, username: user.username, createdAt: user.created_at, lastLoginAt: user.last_login_at },
  });
}

function authBody(value: unknown): { ok: true; username: string; password: string; remember: boolean } | { ok: false; error: string } {
  if (!value || typeof value !== "object") return { ok: false, error: "Requête invalide." };
  const body = value as Record<string, unknown>;
  const username = cleanUsername(body.username);
  if (!username) return { ok: false, error: "Le nom d'utilisateur doit contenir 3 à 20 caractères (lettres, chiffres, _)." };
  const password = cleanPassword(body.password);
  if (!password) return { ok: false, error: "Le mot de passe doit contenir entre 8 et 128 caractères." };
  return { ok: true, username, password, remember: body.remember === true };
}
