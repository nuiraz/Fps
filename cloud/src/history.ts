import { requireUser } from "./auth";
import { error, json, readJson } from "./http";
import { parseHistoryEntry } from "./validate";

const KEEP = 200;

export async function listHistory(request: Request, env: Env): Promise<Response> {
  const user = await requireUser(request, env);
  if (user instanceof Response) return user;
  const limitRaw = Number(new URL(request.url).searchParams.get("limit") ?? "200");
  const limit = Number.isFinite(limitRaw) ? Math.min(200, Math.max(1, Math.floor(limitRaw))) : 200;
  const rows = await env.DB.prepare(
    "SELECT id, action, detail, success, occurred_at FROM history WHERE user_id = ? ORDER BY occurred_at DESC LIMIT ?",
  ).bind(user.id, limit).all<{ id: string; action: string; detail: string; success: number; occurred_at: string }>();
  return json((rows.results ?? []).map((row) => ({
    id: row.id,
    action: row.action,
    detail: row.detail,
    success: row.success === 1,
    occurredAt: row.occurred_at,
  })));
}

export async function postHistory(request: Request, env: Env): Promise<Response> {
  const user = await requireUser(request, env);
  if (user instanceof Response) return user;
  const body = await readJson(request);
  if (!body.ok) return body.response;
  const source = body.value as Record<string, unknown> | null;
  const list = source && Array.isArray(source.entries) ? source.entries : [body.value];
  if (list.length === 0 || list.length > 50) return error("Lot d'historique invalide.", 400);
  const entries = [];
  for (const item of list) {
    const parsed = parseHistoryEntry(item);
    if (!parsed.ok) return error(parsed.error, 400);
    entries.push(parsed.entry);
  }
  const now = new Date().toISOString();
  await env.DB.batch(entries.map((entry) => env.DB.prepare(
    `INSERT OR IGNORE INTO history (id, user_id, action, detail, success, occurred_at, created_at)
     VALUES (?, ?, ?, ?, ?, ?, ?)`,
  ).bind(entry.id, user.id, entry.action, entry.detail, entry.success ? 1 : 0, entry.occurredAt, now)));
  await env.DB.prepare(
    `DELETE FROM history WHERE user_id = ? AND id NOT IN (
       SELECT id FROM history WHERE user_id = ? ORDER BY occurred_at DESC LIMIT ?
     )`,
  ).bind(user.id, user.id, KEEP).run();
  return json({ saved: entries.length });
}
