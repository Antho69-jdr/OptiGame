// Serveur de mise en relation des appels vocaux d'OptiGame (Cloudflare Workers + Durable Objects).
//
// 1. Salons (classe Room, un par code) : les deux PC d'un appel ouvrent une WebSocket sur /v1/rooms/<CODE>?role=host|guest ; le
//    serveur relaie leurs descriptions de connexion WebRTC le temps qu'ils se trouvent, puis ils se déconnectent. La voix ne passe
//    JAMAIS par ici (elle va directement d'un PC à l'autre, chiffrée).
// 2. Connexion avec Steam (classe Login, une par demande) : OpenID 2.0 de Steam dans le navigateur de l'utilisateur ; le serveur
//    vérifie la réponse AUPRÈS DE STEAM et remet à OptiGame (WebSocket /v1/auth/wait) un jeton signé qui porte le numéro de compte
//    Steam (30 jours). Aucun mot de passe ne passe par ici.
// 3. Amis en ligne (classe Presence, une seule) : OptiGame connecté (WebSocket /v1/presence, jeton dans l'en-tête Authorization)
//    voit ses amis Steam qui ont OptiGame ouvert, et peut les appeler (sonnerie = le code d'un salon). Deux comptes ne se voient
//    que s'ils sont AMIS SUR STEAM (liste d'amis demandée à Steam avec la clé d'API de l'auteur ; une liste privée suffit si celle
//    de l'autre est publique) ou AMIS OPTIGAME (demande d'ami acceptée, gardée sur les deux PC). Gardé seulement le temps de la
//    connexion : numéro de compte, nom affiché, liste d'amis Steam, liste d'amis OptiGame envoyée par le PC.
//
// Rien n'est journalisé. Plan gratuit de Cloudflare : rien ne peut être facturé (au-delà du quota quotidien, refus jusqu'au
// lendemain). Secrets du Worker : TOKEN_SECRET (signature des jetons), STEAM_API_KEY (clé d'API Web de Steam).
//
// Protocole des salons (JSON) — envoyé par le serveur : {t:"waiting", expiresAt} à l'hôte, {t:"peer"} aux deux quand l'ami arrive,
// {t:"left"} quand l'autre part, {t:"error", code:"busy"|"unknown"|"expired"|"limit"} avant de fermer ; le reste est relayé tel quel.
// Protocole des amis : serveur → {t:"hello", you, online:[{id,name}], friendsListPublic}, {t:"online", friend}, {t:"offline", id},
// {t:"ring", from, code}, {t:"declined"|"cancelled", from, code}, {t:"callError", to, reason}, {t:"replaced"},
// {t:"friendRequest"|"friendAccepted", from} ;
// client → {t:"call"|"decline"|"cancel", to, code}, {t:"contacts", ids}, {t:"request"|"accept", to}, {t:"ping"} (réponse {t:"pong"}).

const CODE = /^[23456789ABCDEFGHJKMNPQRSTUVWXYZ]{6}$/;
const ROOM = /^\/v1\/rooms\/([23456789ABCDEFGHJKMNPQRSTUVWXYZ]{6})$/;
const STATE = /^[A-Za-z0-9_-]{22,64}$/;
const STEAM_ID = /^7656119\d{10}$/;
const WAIT_MS = 2 * 60 * 1000;
const CONNECT_MS = 2 * 60 * 1000;
const LOGIN_MS = 5 * 60 * 1000;
const TOKEN_DAYS = 30;
const MAX_MESSAGE = 16 * 1024;
const MAX_MESSAGES = 200;
const STEAM_OPENID = "https://steamcommunity.com/openid/login";

export default {
  async fetch(request, env) {
    const url = new URL(request.url);
    const websocket = request.headers.get("Upgrade") === "websocket";

    const room = url.pathname.match(ROOM);
    if (room) {
      if (!websocket) return new Response("WebSocket attendu", { status: 426 });
      const role = url.searchParams.get("role");
      if (role !== "host" && role !== "guest") return new Response("Rôle inconnu", { status: 400 });
      return env.ROOMS.get(env.ROOMS.idFromName(room[1])).fetch(request);
    }

    if (url.pathname === "/v1/auth/steam") return steamStart(url);
    if (url.pathname === "/v1/auth/steam/return") return steamReturn(url, env);
    if (url.pathname === "/v1/auth/wait") {
      const state = url.searchParams.get("state") || "";
      if (!websocket || !STATE.test(state)) return new Response("Demande invalide", { status: 400 });
      return env.LOGINS.get(env.LOGINS.idFromName(state)).fetch(request);
    }

    if (url.pathname === "/v1/presence") {
      if (!websocket) return new Response("WebSocket attendu", { status: 426 });
      const auth = request.headers.get("Authorization") || "";
      const steamId = await verifyToken(env, auth.startsWith("Bearer ") ? auth.slice(7) : "");
      if (!steamId) return new Response("Jeton invalide ou expiré", { status: 401 });
      // Seul le numéro vérifié est transmis (aucun en-tête du client n'est repris).
      const forwarded = new Request("https://presence/connect", { headers: { Upgrade: "websocket", "X-Steam-Id": steamId } });
      return env.PRESENCE.get(env.PRESENCE.idFromName("global")).fetch(forwarded);
    }

    return new Response("OptiGame", { status: 404 });
  },
};

// ===== Connexion avec Steam (OpenID 2.0) =====

function steamStart(url) {
  const state = url.searchParams.get("state") || "";
  if (!STATE.test(state)) return new Response("Demande invalide", { status: 400 });
  const params = new URLSearchParams({
    "openid.ns": "http://specs.openid.net/auth/2.0",
    "openid.mode": "checkid_setup",
    "openid.return_to": `${url.origin}/v1/auth/steam/return?state=${state}`,
    "openid.realm": url.origin,
    "openid.identity": "http://specs.openid.net/auth/2.0/identifier_select",
    "openid.claimed_id": "http://specs.openid.net/auth/2.0/identifier_select",
  });
  return Response.redirect(`${STEAM_OPENID}?${params}`, 302);
}

async function steamReturn(url, env) {
  const p = url.searchParams;
  const state = p.get("state") || "";
  if (!STATE.test(state)) return page("Demande invalide", "Recommencez depuis OptiGame.");
  if (p.get("openid.mode") !== "id_res") return page("Connexion annulée", "Vous pouvez fermer cette page et recommencer depuis OptiGame.");
  const claimed = (p.get("openid.claimed_id") || "").match(/^https:\/\/steamcommunity\.com\/openid\/id\/(7656119\d{10})$/);
  if (p.get("openid.op_endpoint") !== STEAM_OPENID || !claimed
      || p.get("openid.return_to") !== `${url.origin}/v1/auth/steam/return?state=${state}`) {
    return page("Réponse de Steam refusée", "Recommencez depuis OptiGame.");
  }
  // Vérification auprès de Steam lui-même : seule preuve que la réponse vient bien de lui.
  const body = new URLSearchParams();
  for (const [key, value] of p) if (key.startsWith("openid.")) body.set(key, value);
  body.set("openid.mode", "check_authentication");
  const check = await fetch(STEAM_OPENID, { method: "POST", body, headers: { "Content-Type": "application/x-www-form-urlencoded" } });
  if (!(await check.text()).split("\n").includes("is_valid:true")) return page("Réponse de Steam refusée", "Recommencez depuis OptiGame.");

  const steamId = claimed[1];
  const name = await personaName(env, steamId);
  const token = await signToken(env, steamId);
  const delivered = await env.LOGINS.get(env.LOGINS.idFromName(state)).fetch("https://login/deliver", {
    method: "POST",
    body: JSON.stringify({ t: "token", token, id: steamId, name }),
  });
  return delivered.ok
    ? page("OptiGame est connecté à Steam", `Compte : ${name || steamId}. Vous pouvez fermer cette page et revenir dans OptiGame.`)
    : page("Demande expirée", "OptiGame n'attendait plus cette connexion : recommencez depuis OptiGame.");
}

function page(title, text) {
  const escape = s => s.replace(/[&<>"]/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" })[c]);
  const html = `<!doctype html><html lang="fr"><meta charset="utf-8"><meta name="viewport" content="width=device-width">
<title>${escape(title)}</title><body style="font-family:Segoe UI,sans-serif;background:#0E1014;color:#E8EAED;display:grid;place-items:center;height:90vh;margin:0">
<main style="max-width:32rem;padding:1rem"><h1 style="font-size:1.4rem">${escape(title)}</h1><p>${escape(text)}</p></main></body></html>`;
  return new Response(html, {
    headers: { "Content-Type": "text/html; charset=utf-8", "Content-Security-Policy": "default-src 'none'; style-src 'unsafe-inline'" },
  });
}

// ===== Jetons signés : « <steamid>.<expiration>.<signature> » =====

async function hmacKey(env) {
  return crypto.subtle.importKey("raw", new TextEncoder().encode(env.TOKEN_SECRET), { name: "HMAC", hash: "SHA-256" }, false, ["sign", "verify"]);
}

function base64url(bytes) {
  return btoa(String.fromCharCode(...new Uint8Array(bytes))).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
}

async function signToken(env, steamId) {
  const payload = `${steamId}.${Date.now() + TOKEN_DAYS * 24 * 3600 * 1000}`;
  const signature = await crypto.subtle.sign("HMAC", await hmacKey(env), new TextEncoder().encode(payload));
  return `${payload}.${base64url(signature)}`;
}

async function verifyToken(env, token) {
  const parts = token.split(".");
  if (parts.length !== 3 || !STEAM_ID.test(parts[0]) || !/^\d{13}$/.test(parts[1]) || Number(parts[1]) < Date.now()) return null;
  let signature;
  try {
    signature = Uint8Array.from(atob(parts[2].replace(/-/g, "+").replace(/_/g, "/")), c => c.charCodeAt(0));
  } catch {
    return null;
  }
  const ok = await crypto.subtle.verify("HMAC", await hmacKey(env), signature, new TextEncoder().encode(`${parts[0]}.${parts[1]}`));
  return ok ? parts[0] : null;
}

// ===== API Web de Steam =====

async function personaName(env, steamId) {
  try {
    const r = await fetch(`https://api.steampowered.com/ISteamUser/GetPlayerSummaries/v2/?key=${env.STEAM_API_KEY}&steamids=${steamId}`);
    if (!r.ok) return "";
    const players = (await r.json()).response?.players || [];
    return String(players[0]?.personaname || "").slice(0, 64);
  } catch {
    return "";
  }
}

// Liste d'amis ; null si Steam la refuse (liste privée).
async function friendIds(env, steamId) {
  try {
    const r = await fetch(`https://api.steampowered.com/ISteamUser/GetFriendList/v1/?key=${env.STEAM_API_KEY}&steamid=${steamId}&relationship=friend`);
    if (!r.ok) return null;
    return ((await r.json()).friendslist?.friends || []).map(f => f.steamid).filter(id => STEAM_ID.test(id));
  } catch {
    return null;
  }
}

// ===== Salons d'appel =====

export class Room {
  constructor(state) {
    this.state = state;
  }

  async fetch(request) {
    const role = new URL(request.url).searchParams.get("role");
    const host = this.state.getWebSockets("host")[0];
    const guest = this.state.getWebSockets("guest")[0];
    const [client, server] = Object.values(new WebSocketPair());

    let error = null;
    if (role === "host" && (host || guest)) error = "busy";
    else if (role === "guest" && !host) error = "unknown";
    else if (role === "guest" && guest) error = "busy";
    if (error) {
      server.accept();
      server.send(JSON.stringify({ t: "error", code: error }));
      server.close(4000, error);
      return new Response(null, { status: 101, webSocket: client });
    }

    this.state.acceptWebSocket(server, [role]);
    const now = Date.now();
    if (role === "host") {
      await this.state.storage.put("count", 0);
      await this.state.storage.setAlarm(now + WAIT_MS);
      server.send(JSON.stringify({ t: "waiting", expiresAt: now + WAIT_MS }));
    } else {
      await this.state.storage.setAlarm(now + CONNECT_MS);
      const peer = JSON.stringify({ t: "peer" });
      server.send(peer);
      host.send(peer);
    }
    return new Response(null, { status: 101, webSocket: client });
  }

  async webSocketMessage(ws, message) {
    if (typeof message !== "string" || message.length > MAX_MESSAGE) return this.closeAll("limit");
    const count = ((await this.state.storage.get("count")) || 0) + 1;
    if (count > MAX_MESSAGES) return this.closeAll("limit");
    await this.state.storage.put("count", count);
    const role = this.state.getTags(ws)[0];
    const other = this.state.getWebSockets(role === "host" ? "guest" : "host")[0];
    if (other) other.send(message);
  }

  async webSocketClose(ws) {
    try { ws.close(1000, "bye"); } catch { /* déjà fermée */ }
    for (const other of this.state.getWebSockets()) {
      if (other === ws) continue;
      try {
        other.send(JSON.stringify({ t: "left" }));
        other.close(1000, "left");
      } catch { /* déjà fermée */ }
    }
    await this.clear();
  }

  async webSocketError(ws) {
    await this.webSocketClose(ws);
  }

  async alarm() {
    await this.closeAll("expired");
  }

  async closeAll(code) {
    for (const ws of this.state.getWebSockets()) {
      try {
        ws.send(JSON.stringify({ t: "error", code }));
        ws.close(4000, code);
      } catch { /* déjà fermée */ }
    }
    await this.clear();
  }

  async clear() {
    await this.state.storage.deleteAlarm();
    await this.state.storage.deleteAll();
  }
}

// ===== Attente d'une connexion avec Steam (une par demande) =====

export class Login {
  constructor(state) {
    this.state = state;
  }

  async fetch(request) {
    if (request.headers.get("Upgrade") === "websocket") {
      for (const old of this.state.getWebSockets()) { try { old.close(1000, "replaced"); } catch { /* fermée */ } }
      const [client, server] = Object.values(new WebSocketPair());
      this.state.acceptWebSocket(server);
      await this.state.storage.setAlarm(Date.now() + LOGIN_MS);
      return new Response(null, { status: 101, webSocket: client });
    }
    const waiting = this.state.getWebSockets()[0];
    if (!waiting) return new Response("Personne n'attend", { status: 404 });
    waiting.send(await request.text());
    waiting.close(1000, "done");
    await this.state.storage.deleteAll();
    return new Response("ok");
  }

  async webSocketMessage() { /* rien à recevoir */ }

  async webSocketClose(ws) {
    try { ws.close(1000, "bye"); } catch { /* déjà fermée */ }
    await this.state.storage.deleteAll();
  }

  async alarm() {
    for (const ws of this.state.getWebSockets()) { try { ws.close(4000, "expired"); } catch { /* fermée */ } }
    await this.state.storage.deleteAll();
  }
}

// ===== Amis en ligne (une seule instance) =====
//
// Deux comptes sont « liés » s'ils sont amis sur Steam (liste de l'un OU de l'autre), ou s'ils se sont ajoutés MUTUELLEMENT comme
// amis OptiGame (listes « contacts » envoyées par chaque PC, qui les garde : le serveur ne les conserve que le temps de la connexion).
// Une demande d'ami n'est remise que si le destinataire est en ligne, et l'expéditeur n'apprend rien (ni s'il est en ligne, ni s'il
// refuse) : il ne le voit que si l'autre accepte.

const MAX_CONTACTS = 500;
const MAX_REQUESTS = 20; // demandes d'ami par connexion

function linked(a, ua, b, ub) {
  if (!ua || !ub) return false;
  const steam = (ua.friends && ua.friends.includes(b)) || (ub.friends && ub.friends.includes(a));
  const mutual = (ua.contacts || []).includes(b) && (ub.contacts || []).includes(a);
  return steam || mutual;
}

export class Presence {
  constructor(state, env) {
    this.state = state;
    this.env = env;
    this.requests = new Map();
    this.state.setWebSocketAutoResponse(new WebSocketRequestResponsePair('{"t":"ping"}', '{"t":"pong"}'));
  }

  async fetch(request) {
    const id = request.headers.get("X-Steam-Id");
    if (!STEAM_ID.test(id || "")) return new Response("Compte manquant", { status: 400 });
    // Une connexion par compte : la nouvelle remplace l'ancienne (OptiGame relancé, autre PC).
    for (const old of this.state.getWebSockets(id)) {
      try { old.send(JSON.stringify({ t: "replaced" })); old.close(4001, "replaced"); } catch { /* fermée */ }
    }
    const [friends, name] = await Promise.all([friendIds(this.env, id), personaName(this.env, id)]);
    const me = { name, friends, contacts: [] };
    await this.state.storage.put(`u:${id}`, me);
    const [client, server] = Object.values(new WebSocketPair());
    this.state.acceptWebSocket(server, [id]);

    const online = [];
    for (const other of await this.linkedOnline(id, me)) {
      online.push({ id: other.id, name: other.name });
      this.sendTo(other.id, { t: "online", friend: { id, name } });
    }
    server.send(JSON.stringify({ t: "hello", you: { id, name }, online, friendsListPublic: friends !== null }));
    return new Response(null, { status: 101, webSocket: client });
  }

  // Comptes connectés liés à `id`.
  async linkedOnline(id, me) {
    const ids = [...new Set(this.state.getWebSockets().map(ws => this.state.getTags(ws)[0]))].filter(other => other !== id);
    const result = [];
    for (let i = 0; i < ids.length; i += 100) {
      const users = await this.state.storage.get(ids.slice(i, i + 100).map(other => `u:${other}`));
      for (const other of ids.slice(i, i + 100)) {
        const user = users.get(`u:${other}`);
        if (linked(id, me, other, user)) result.push({ id: other, name: user.name });
      }
    }
    return result;
  }

  async areLinked(a, b) {
    const users = await this.state.storage.get([`u:${a}`, `u:${b}`]);
    return linked(a, users.get(`u:${a}`), b, users.get(`u:${b}`));
  }

  sendTo(id, message) {
    for (const ws of this.state.getWebSockets(id)) { try { ws.send(JSON.stringify(message)); } catch { /* fermée */ } }
  }

  async webSocketMessage(ws, raw) {
    if (typeof raw !== "string" || raw.length > 32 * 1024) return;
    let m;
    try { m = JSON.parse(raw); } catch { return; }
    const from = this.state.getTags(ws)[0];

    if (m.t === "contacts" && Array.isArray(m.ids)) return this.setContacts(from, m.ids);

    if (m.t === "request" || m.t === "accept") {
      if (!STEAM_ID.test(m.to || "") || m.to === from) return;
      const count = (this.requests.get(from) || 0) + 1;
      this.requests.set(from, count);
      if (count > MAX_REQUESTS) return;
      const user = await this.state.storage.get(`u:${from}`);
      this.sendTo(m.to, { t: m.t === "request" ? "friendRequest" : "friendAccepted", from: { id: from, name: user?.name || "" } });
      return;
    }

    if (!["call", "decline", "cancel"].includes(m.t) || !STEAM_ID.test(m.to || "") || !CODE.test(m.code || "")) return;
    const online = this.state.getWebSockets(m.to).length > 0;
    if (!online || !(await this.areLinked(from, m.to))) {
      if (m.t === "call") ws.send(JSON.stringify({ t: "callError", to: m.to, reason: online ? "notFriend" : "offline" }));
      return;
    }
    const user = await this.state.storage.get(`u:${from}`);
    const type = { call: "ring", decline: "declined", cancel: "cancelled" }[m.t];
    this.sendTo(m.to, { t: type, from: { id: from, name: user?.name || "" }, code: m.code });
  }

  // Liste d'amis OptiGame du PC : ceux qui deviennent (ou cessent d'être) liés en sont prévenus, des deux côtés.
  async setContacts(id, ids) {
    const contacts = [...new Set(ids.filter(c => typeof c === "string" && STEAM_ID.test(c) && c !== id))].slice(0, MAX_CONTACTS);
    const me = await this.state.storage.get(`u:${id}`);
    if (!me) return;
    const before = new Set((await this.linkedOnline(id, me)).map(u => u.id));
    me.contacts = contacts;
    await this.state.storage.put(`u:${id}`, me);
    const after = await this.linkedOnline(id, me);
    for (const other of after) {
      if (before.has(other.id)) continue;
      this.sendTo(id, { t: "online", friend: { id: other.id, name: other.name } });
      this.sendTo(other.id, { t: "online", friend: { id, name: me.name } });
    }
    const still = new Set(after.map(u => u.id));
    for (const gone of before) {
      if (still.has(gone)) continue;
      this.sendTo(id, { t: "offline", id: gone });
      this.sendTo(gone, { t: "offline", id });
    }
  }

  async webSocketClose(ws) {
    try { ws.close(1000, "bye"); } catch { /* déjà fermée */ }
    const id = this.state.getTags(ws)[0];
    if (this.state.getWebSockets(id).some(other => other !== ws)) return; // remplacée par une connexion plus récente
    this.requests.delete(id);
    const me = await this.state.storage.get(`u:${id}`);
    if (me) for (const friend of await this.linkedOnline(id, me)) this.sendTo(friend.id, { t: "offline", id });
    await this.state.storage.delete(`u:${id}`);
  }

  async webSocketError(ws) {
    await this.webSocketClose(ws);
  }
}
