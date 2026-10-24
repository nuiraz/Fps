import { login, logout, me, register } from "./auth";
import { listHistory, postHistory } from "./history";
import { error, json, log, logError, noContentCors } from "./http";
import { deletePreset, listPresets, upsertPreset } from "./presets";
import { getSettings, putSettings } from "./settings";

export default {
  async fetch(request, env): Promise<Response> {
    const url = new URL(request.url);
    const path = url.pathname.replace(/\/+$/, "") || "/";
    const method = request.method.toUpperCase();
    if (method === "OPTIONS") return noContentCors();
    try {
      if (method === "GET" && (path === "/" || path === "/api/health")) return json({ name: "fps-api", ok: true });
      if (path === "/api/register" && method === "POST") return register(request, env);
      if (path === "/api/login" && method === "POST") return login(request, env);
      if (path === "/api/logout" && method === "POST") return logout(request, env);
      if (path === "/api/me" && method === "GET") return me(request, env);
      if (path === "/api/presets" && method === "GET") return listPresets(request, env);
      if (path === "/api/settings" && method === "GET") return getSettings(request, env);
      if (path === "/api/settings" && method === "PUT") return putSettings(request, env);
      if (path === "/api/history" && method === "GET") return listHistory(request, env);
      if (path === "/api/history" && method === "POST") return postHistory(request, env);
      const preset = /^\/api\/presets\/([^/]+)$/.exec(path);
      if (preset && method === "PUT") return upsertPreset(request, env, decodeURIComponent(preset[1] ?? ""));
      if (preset && method === "DELETE") return deletePreset(request, env, decodeURIComponent(preset[1] ?? ""));
      log("not_found", { method, path });
      return error("Introuvable.", 404);
    } catch (cause) {
      logError("request_failed", cause);
      return error("Erreur interne.", 500);
    }
  },
} satisfies ExportedHandler<Env>;
