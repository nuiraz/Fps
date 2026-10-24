import { log } from "./http";

const WINDOW_MS = 15 * 60 * 1000;
const IP_LIMIT = 8;
const USER_LIMIT = 10;

export async function assertLoginAllowed(db: D1Database, ip: string, username: string): Promise<Response | null> {
  const now = new Date();
  const dayAgo = new Date(now.getTime() - 24 * 60 * 60 * 1000).toISOString();
  const windowStart = new Date(now.getTime() - WINDOW_MS).toISOString();
  await db.prepare("DELETE FROM login_attempts WHERE created_at < ?").bind(dayAgo).run();
  const ipCount = await db.prepare(
    "SELECT COUNT(*) AS n FROM login_attempts WHERE success = 0 AND ip = ? AND created_at >= ?",
  ).bind(ip, windowStart).first<{ n: number }>();
  const userCount = await db.prepare(
    "SELECT COUNT(*) AS n FROM login_attempts WHERE success = 0 AND username = ? AND created_at >= ?",
  ).bind(username, windowStart).first<{ n: number }>();
  if ((ipCount?.n ?? 0) >= IP_LIMIT || (userCount?.n ?? 0) >= USER_LIMIT) {
    log("login_rate_limited", { ip });
    return new Response(JSON.stringify({ error: "Trop de tentatives. Réessayez plus tard." }), {
      status: 429,
      headers: {
        "Content-Type": "application/json; charset=utf-8",
        "Retry-After": "900",
        "Access-Control-Allow-Origin": "*",
        "Cache-Control": "no-store",
      },
    });
  }
  return null;
}

export async function recordAttempt(db: D1Database, ip: string, username: string, success: boolean): Promise<void> {
  await db.prepare(
    "INSERT INTO login_attempts (ip, username, success, created_at) VALUES (?, ?, ?, ?)",
  ).bind(ip, username, success ? 1 : 0, new Date().toISOString()).run();
}
