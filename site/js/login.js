const config = window.GAMEBOOSTER_CONFIG;
const base = (config.apiBaseUrl || "").replace(/\/$/, "");
const warning = document.getElementById("warning");
const message = document.getElementById("message");
const account = document.getElementById("account");
const form = document.getElementById("form");

if (base.includes("<subdomain>")) warning.hidden = false;

form.addEventListener("submit", (event) => {
  event.preventDefault();
  authenticate("/api/login");
});
document.getElementById("register").addEventListener("click", () => authenticate("/api/register"));
document.getElementById("logout").addEventListener("click", logout);

const existing = sessionStorage.getItem("gamebooster-token");
if (existing && !base.includes("<subdomain>")) {
  showAccount(existing).catch(() => sessionStorage.removeItem("gamebooster-token"));
}

async function authenticate(path) {
  message.textContent = "";
  if (base.includes("<subdomain>")) {
    message.textContent = "Configurez d'abord l'URL du Worker.";
    return;
  }
  const username = document.getElementById("username").value.trim();
  const password = document.getElementById("password").value;
  try {
    const response = await fetch(base + path, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ username, password, remember: false })
    });
    const payload = await response.json().catch(() => ({}));
    if (!response.ok) {
      message.textContent = payload.error || "Connexion impossible.";
      return;
    }
    sessionStorage.setItem("gamebooster-token", payload.token);
    await showAccount(payload.token);
  } catch {
    message.textContent = "Impossible de joindre le serveur.";
  }
}

async function showAccount(token) {
  const headers = { Authorization: "Bearer " + token };
  const me = await fetch(base + "/api/me", { headers });
  if (me.status === 401) {
    sessionStorage.removeItem("gamebooster-token");
    return;
  }
  const user = await me.json();
  const presets = await (await fetch(base + "/api/presets", { headers })).json();
  const history = await (await fetch(base + "/api/history?limit=20", { headers })).json();
  form.hidden = true;
  account.hidden = false;
  document.getElementById("who").textContent = user.username;
  document.getElementById("created").textContent = "Compte créé : " + format(user.createdAt);
  fill("presets", Array.isArray(presets) && presets.length ? presets.map((preset) => preset.name) : ["Aucun preset personnel."]);
  fill("history", Array.isArray(history) && history.length ? history.map((entry) => format(entry.occurredAt) + " — " + entry.detail) : ["Aucun historique."]);
}

async function logout() {
  const token = sessionStorage.getItem("gamebooster-token");
  if (token) {
    await fetch(base + "/api/logout", { method: "POST", headers: { Authorization: "Bearer " + token } }).catch(() => {});
  }
  sessionStorage.removeItem("gamebooster-token");
  location.reload();
}

function fill(id, lines) {
  const host = document.getElementById(id);
  host.replaceChildren();
  for (const line of lines) {
    const row = document.createElement("div");
    row.className = "row";
    row.textContent = line;
    host.appendChild(row);
  }
}

function format(value) {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return "—";
  return new Intl.DateTimeFormat("fr-FR", { dateStyle: "short", timeStyle: "short" }).format(date);
}
