const USERNAME = /^[a-z0-9_]{3,20}$/;
const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const TOGGLE = new Set(["leave", "enable", "disable"]);
const POWER = new Set(["leave", "ultimate", "high", "balanced"]);
const ACTIONS = new Set(["boost", "restore", "cleanup", "network", "startup", "preset", "sync"]);
const PROCESS_NAME = /^[A-Za-z0-9_.-]{1,80}$/;
const EXE_PATH = /^[A-Za-z]:\\[^<>:"|?*\r\n]{1,252}\.exe$/i;
const HOST = /^[A-Za-z0-9:._-]{1,253}$/;
const APPS = new Set([
  "discord", "spotify", "chrome", "msedge", "firefox", "opera", "brave", "onedrive", "teams", "ms-teams",
  "slack", "skype", "zoom", "telegram", "whatsapp", "signal", "wallpaper64", "sharex", "qbittorrent",
  "utorrent", "epicwebhelper", "steamwebhelper",
]);

const TWEAK_KEYS = [
  "powerPlan", "gameMode", "hags", "gameDvr", "fullscreenOptimizations", "gamePriority", "nagle",
  "visualEffects", "cleanTemp", "cleanShaderCache", "emptyRecycleBin", "flushDns", "closeBackgroundApps",
  "createRestorePoint",
] as const;

export type Tweaks = Record<(typeof TWEAK_KEYS)[number], string | boolean>;

export type Preferences = {
  activePresetId: string | null;
  watchedGames: string[];
  fullscreenOptimizationPaths: string[];
  precheckBackgroundApps: string[];
  pingHost: string;
  pingCount: number;
};

export type SettingsDocument = {
  updatedAt: string;
  preferences: Preferences;
};

export function normalizeTime(input: unknown, now = Date.now()): string | null {
  if (typeof input !== "string" || input.length > 40) return null;
  const parsed = Date.parse(input);
  if (Number.isNaN(parsed)) return null;
  const value = parsed > now + 5 * 60 * 1000 ? now : parsed;
  return new Date(value).toISOString();
}

export function isOlder(incoming: string, stored: string): boolean {
  return Date.parse(incoming) < Date.parse(stored);
}

export function cleanUsername(value: unknown): string | null {
  if (typeof value !== "string") return null;
  const name = value.trim().toLowerCase();
  return USERNAME.test(name) ? name : null;
}

export function cleanPassword(value: unknown): string | null {
  if (typeof value !== "string" || value.length < 8 || value.length > 128 || value.includes("\0")) return null;
  return value;
}

export function isGuid(value: string): boolean {
  return GUID.test(value);
}

export function defaultPreferences(): Preferences {
  return {
    activePresetId: "builtin-competitive",
    watchedGames: ["FortniteClient-Win64-Shipping", "cs2", "r5apex", "VALORANT-Win64-Shipping", "RocketLeague"],
    fullscreenOptimizationPaths: [],
    precheckBackgroundApps: ["discord", "spotify", "epicwebhelper", "steamwebhelper"],
    pingHost: "1.1.1.1",
    pingCount: 4,
  };
}

export function defaultSettings(now = "1970-01-01T00:00:00.000Z"): SettingsDocument {
  return { updatedAt: now, preferences: defaultPreferences() };
}

export function parseTweaks(value: unknown): { ok: true; tweaks: Tweaks } | { ok: false; error: string } {
  if (!value || typeof value !== "object" || Array.isArray(value)) return { ok: false, error: "Réglages de preset invalides." };
  const source = value as Record<string, unknown>;
  for (const key of Object.keys(source)) {
    if (!TWEAK_KEYS.includes(key as (typeof TWEAK_KEYS)[number])) return { ok: false, error: "Champ de preset inconnu." };
  }
  const tweaks: Tweaks = {
    powerPlan: typeof source.powerPlan === "string" && POWER.has(source.powerPlan) ? source.powerPlan : "leave",
    gameMode: toggle(source.gameMode),
    hags: toggle(source.hags),
    gameDvr: toggle(source.gameDvr),
    fullscreenOptimizations: toggle(source.fullscreenOptimizations),
    gamePriority: toggle(source.gamePriority),
    nagle: toggle(source.nagle),
    visualEffects: toggle(source.visualEffects),
    cleanTemp: source.cleanTemp === true,
    cleanShaderCache: source.cleanShaderCache === true,
    emptyRecycleBin: source.emptyRecycleBin === true,
    flushDns: source.flushDns === true,
    closeBackgroundApps: source.closeBackgroundApps === true,
    createRestorePoint: source.createRestorePoint !== false,
  };
  if (source.powerPlan !== undefined && (typeof source.powerPlan !== "string" || !POWER.has(source.powerPlan))) {
    return { ok: false, error: "Plan d'alimentation invalide." };
  }
  for (const key of ["gameMode", "hags", "gameDvr", "fullscreenOptimizations", "gamePriority", "nagle", "visualEffects"]) {
    if (source[key] !== undefined && (typeof source[key] !== "string" || !TOGGLE.has(source[key] as string))) {
      return { ok: false, error: "Interrupteur invalide." };
    }
  }
  const encoded = JSON.stringify(tweaks);
  if (encoded.length > 16_000) return { ok: false, error: "Preset trop volumineux." };
  return { ok: true, tweaks };
}

export function parsePreferences(value: unknown): { ok: true; preferences: Preferences } | { ok: false; error: string } {
  if (!value || typeof value !== "object" || Array.isArray(value)) return { ok: false, error: "Préférences invalides." };
  const source = value as Record<string, unknown>;
  const allowed = new Set(["activePresetId", "watchedGames", "fullscreenOptimizationPaths", "precheckBackgroundApps", "pingHost", "pingCount"]);
  for (const key of Object.keys(source)) {
    if (!allowed.has(key)) return { ok: false, error: "Champ de préférence inconnu." };
  }
  let active: string | null = null;
  if (source.activePresetId != null) {
    if (typeof source.activePresetId !== "string" || source.activePresetId.length > 64) return { ok: false, error: "Preset actif invalide." };
    active = source.activePresetId;
  }
  const games = stringList(source.watchedGames, 30, (item) => PROCESS_NAME.test(item));
  const paths = stringList(source.fullscreenOptimizationPaths, 20, (item) => EXE_PATH.test(item));
  const apps = stringList(source.precheckBackgroundApps, 40, (item) => APPS.has(item.toLowerCase()));
  if (!games || !paths || !apps) return { ok: false, error: "Liste de préférences invalide." };
  if (typeof source.pingHost !== "string" || !HOST.test(source.pingHost)) return { ok: false, error: "Hôte de ping invalide." };
  if (typeof source.pingCount !== "number" || !Number.isInteger(source.pingCount) || source.pingCount < 1 || source.pingCount > 10) {
    return { ok: false, error: "Nombre de pings invalide." };
  }
  const preferences: Preferences = {
    activePresetId: active,
    watchedGames: games,
    fullscreenOptimizationPaths: paths,
    precheckBackgroundApps: apps.map((item) => item.toLowerCase()),
    pingHost: source.pingHost,
    pingCount: source.pingCount,
  };
  if (JSON.stringify(preferences).length > 60_000) return { ok: false, error: "Préférences trop volumineuses." };
  return { ok: true, preferences };
}

export function parseHistoryEntry(value: unknown, now = Date.now()): {
  ok: true;
  entry: { id: string; action: string; detail: string; success: boolean; occurredAt: string };
} | { ok: false; error: string } {
  if (!value || typeof value !== "object" || Array.isArray(value)) return { ok: false, error: "Entrée d'historique invalide." };
  const source = value as Record<string, unknown>;
  if (typeof source.id !== "string" || !isGuid(source.id)) return { ok: false, error: "Identifiant d'historique invalide." };
  if (typeof source.action !== "string" || !ACTIONS.has(source.action)) return { ok: false, error: "Action d'historique invalide." };
  if (typeof source.detail !== "string" || source.detail.length > 4000) return { ok: false, error: "Détail d'historique invalide." };
  if (typeof source.success !== "boolean") return { ok: false, error: "Résultat d'historique invalide." };
  const occurredAt = normalizeTime(source.occurredAt, now);
  if (!occurredAt) return { ok: false, error: "Date d'historique invalide." };
  return { ok: true, entry: { id: source.id.toLowerCase(), action: source.action, detail: source.detail, success: source.success, occurredAt } };
}

function toggle(value: unknown): string {
  return typeof value === "string" && TOGGLE.has(value) ? value : "leave";
}

function stringList(value: unknown, max: number, accept: (item: string) => boolean): string[] | null {
  if (!Array.isArray(value) || value.length > max) return null;
  const items: string[] = [];
  for (const item of value) {
    if (typeof item !== "string" || !accept(item)) return null;
    items.push(item);
  }
  return items;
}
