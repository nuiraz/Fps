import { requireUser } from "./auth";
import { error, json, readJson } from "./http";
import { isGuid, isOlder, normalizeTime, parseTweaks, type Tweaks } from "./validate";

type PresetRow = {
  id: string;
  name: string;
  description: string;
  tweaks_json: string;
  created_at: string;
  updated_at: string;
  deleted: number;
};

export async function listPresets(request: Request, env: Env): Promise<Response> {
  const user = await requireUser(request, env);
  if (user instanceof Response) return user;
  const includeDeleted = new URL(request.url).searchParams.get("includeDeleted") === "1";
  const query = includeDeleted
    ? "SELECT id, name, description, tweaks_json, created_at, updated_at, deleted FROM presets WHERE user_id = ? ORDER BY updated_at DESC"
    : "SELECT id, name, description, tweaks_json, created_at, updated_at, deleted FROM presets WHERE user_id = ? AND deleted = 0 ORDER BY updated_at DESC";
  const rows = await env.DB.prepare(query).bind(user.id).all<PresetRow>();
  return json((rows.results ?? []).map(toPreset));
}

export async function upsertPreset(request: Request, env: Env, id: string): Promise<Response> {
  const user = await requireUser(request, env);
  if (user instanceof Response) return user;
  if (!isGuid(id) || id.startsWith("builtin-")) return error("Identifiant de preset invalide.", 400);
  const body = await readJson(request);
  if (!body.ok) return body.response;
  const parsed = parsePreset(body.value, id);
  if (!parsed.ok) return error(parsed.error, 400);
  const current = await load(env, user.id, id);
  if (current && isOlder(parsed.updatedAt, current.updated_at)) {
    return json({ error: "Conflit de synchronisation.", preset: toPreset(current) }, 409);
  }
  const createdAt = current?.created_at ?? parsed.createdAt;
  await env.DB.prepare(
    `INSERT INTO presets (id, user_id, name, description, tweaks_json, created_at, updated_at, deleted)
     VALUES (?, ?, ?, ?, ?, ?, ?, ?)
     ON CONFLICT(user_id, id) DO UPDATE SET
       name = excluded.name,
       description = excluded.description,
       tweaks_json = excluded.tweaks_json,
       updated_at = excluded.updated_at,
       deleted = excluded.deleted`,
  ).bind(id, user.id, parsed.name, parsed.description, JSON.stringify(parsed.tweaks), createdAt, parsed.updatedAt, parsed.deleted ? 1 : 0).run();
  const saved = await load(env, user.id, id);
  return json(saved ? toPreset(saved) : { id });
}

export async function deletePreset(request: Request, env: Env, id: string): Promise<Response> {
  const user = await requireUser(request, env);
  if (user instanceof Response) return user;
  if (!isGuid(id)) return error("Identifiant de preset invalide.", 400);
  const body = await readJson(request);
  if (!body.ok) return body.response;
  const source = (body.value ?? {}) as Record<string, unknown>;
  const fromQuery = new URL(request.url).searchParams.get("updatedAt");
  const updatedAt = normalizeTime(source.updatedAt ?? fromQuery);
  if (!updatedAt) return error("Date de mise à jour invalide.", 400);
  const current = await load(env, user.id, id);
  if (!current) return error("Introuvable.", 404);
  if (isOlder(updatedAt, current.updated_at)) {
    return json({ error: "Conflit de synchronisation.", preset: toPreset(current) }, 409);
  }
  await env.DB.prepare("UPDATE presets SET deleted = 1, updated_at = ? WHERE user_id = ? AND id = ?").bind(updatedAt, user.id, id).run();
  const saved = await load(env, user.id, id);
  return json(saved ? toPreset(saved) : { id, deleted: true, updatedAt });
}

function parsePreset(value: unknown, id: string): {
  ok: true;
  name: string;
  description: string;
  tweaks: Tweaks;
  createdAt: string;
  updatedAt: string;
  deleted: boolean;
} | { ok: false; error: string } {
  if (!value || typeof value !== "object") return { ok: false, error: "Preset invalide." };
  const source = value as Record<string, unknown>;
  if (source.id !== undefined && source.id !== id) return { ok: false, error: "Identifiant de preset invalide." };
  if (typeof source.name !== "string" || source.name.trim().length < 1 || source.name.trim().length > 60) {
    return { ok: false, error: "Le nom du preset doit contenir entre 1 et 60 caractères." };
  }
  const description = typeof source.description === "string" ? source.description.trim() : "";
  if (description.length > 300) return { ok: false, error: "Description trop longue." };
  const tweaks = parseTweaks(source.tweaks);
  if (!tweaks.ok) return tweaks;
  const updatedAt = normalizeTime(source.updatedAt);
  const createdAt = normalizeTime(source.createdAt) ?? updatedAt;
  if (!updatedAt || !createdAt) return { ok: false, error: "Date de preset invalide." };
  return { ok: true, name: source.name.trim(), description, tweaks: tweaks.tweaks, createdAt, updatedAt, deleted: source.deleted === true };
}

async function load(env: Env, userId: string, id: string): Promise<PresetRow | null> {
  return env.DB.prepare(
    "SELECT id, name, description, tweaks_json, created_at, updated_at, deleted FROM presets WHERE user_id = ? AND id = ?",
  ).bind(userId, id).first<PresetRow>();
}

function toPreset(row: PresetRow): Record<string, unknown> {
  let tweaks: unknown = {};
  try { tweaks = JSON.parse(row.tweaks_json) as unknown; } catch { tweaks = {}; }
  return {
    id: row.id,
    name: row.name,
    description: row.description,
    tweaks,
    createdAt: row.created_at,
    updatedAt: row.updated_at,
    deleted: row.deleted === 1,
  };
}
