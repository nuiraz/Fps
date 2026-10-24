import { requireUser } from "./auth";
import { error, json, readJson } from "./http";
import { defaultSettings, isOlder, normalizeTime, parsePreferences, type SettingsDocument } from "./validate";

export async function getSettings(request: Request, env: Env): Promise<Response> {
  const user = await requireUser(request, env);
  if (user instanceof Response) return user;
  const row = await env.DB.prepare("SELECT settings_json, updated_at FROM settings WHERE user_id = ?").bind(user.id).first<{
    settings_json: string;
    updated_at: string;
  }>();
  if (!row) return json(defaultSettings());
  return json(readStored(row.settings_json, row.updated_at));
}

export async function putSettings(request: Request, env: Env): Promise<Response> {
  const user = await requireUser(request, env);
  if (user instanceof Response) return user;
  const body = await readJson(request);
  if (!body.ok) return body.response;
  if (!body.value || typeof body.value !== "object") return error("Réglages invalides.", 400);
  const source = body.value as Record<string, unknown>;
  const preferences = parsePreferences(source.preferences);
  if (!preferences.ok) return error(preferences.error, 400);
  const updatedAt = normalizeTime(source.updatedAt);
  if (!updatedAt) return error("Date de réglages invalide.", 400);
  const document: SettingsDocument = { updatedAt, preferences: preferences.preferences };
  if (JSON.stringify(document).length > 64_000) return error("Réglages trop volumineux.", 400);
  const current = await env.DB.prepare("SELECT settings_json, updated_at FROM settings WHERE user_id = ?").bind(user.id).first<{
    settings_json: string;
    updated_at: string;
  }>();
  if (current && isOlder(updatedAt, current.updated_at)) {
    return json({ error: "Conflit de synchronisation.", settings: readStored(current.settings_json, current.updated_at) }, 409);
  }
  await env.DB.prepare(
    `INSERT INTO settings (user_id, settings_json, updated_at) VALUES (?, ?, ?)
     ON CONFLICT(user_id) DO UPDATE SET settings_json = excluded.settings_json, updated_at = excluded.updated_at`,
  ).bind(user.id, JSON.stringify(document), updatedAt).run();
  return json(document);
}

function readStored(raw: string, updatedAt: string): SettingsDocument {
  try {
    const parsed = JSON.parse(raw) as SettingsDocument;
    if (parsed?.preferences) return { ...parsed, updatedAt: parsed.updatedAt || updatedAt };
  } catch {
    // document illisible : renvoyer un défaut horodaté
  }
  return defaultSettings(updatedAt);
}
