// Serveur de mise en relation des appels vocaux d'OptiGame (Cloudflare Workers + un Durable Object par code).
//
// Rôle : les deux PC d'un appel ouvrent une WebSocket sur /v1/rooms/<CODE>?role=host|guest ; le serveur relaie leurs messages
// (descriptions de connexion WebRTC et adresses) le temps qu'ils se trouvent, puis ils se déconnectent. La voix ne passe JAMAIS
// par ici (elle va directement d'un PC à l'autre, chiffrée). Rien n'est journalisé ni gardé : le salon est effacé dès qu'un des
// deux part, ou à son expiration.
//
// Limites : 2 personnes par salon ; l'hôte attend son ami 2 minutes au plus ; une fois l'ami arrivé, 2 minutes pour se connecter ;
// messages de 16 Ko au plus, 200 par salon. Le plan gratuit de Cloudflare ne peut rien facturer : au-delà de son quota quotidien,
// les appels sont simplement refusés jusqu'au lendemain.
//
// Protocole (JSON) — envoyé par le serveur : {t:"waiting", expiresAt} à l'hôte, {t:"peer"} aux deux quand l'ami arrive,
// {t:"left"} quand l'autre part, {t:"error", code:"busy"|"unknown"|"expired"|"limit"} avant de fermer. Tout le reste est relayé
// tel quel à l'autre PC (OptiGame envoie {t:"sdp"}, {t:"ice"}, {t:"done"}).

const CODE = /^\/v1\/rooms\/([23456789ABCDEFGHJKMNPQRSTUVWXYZ]{6})$/;
const WAIT_MS = 2 * 60 * 1000;
const CONNECT_MS = 2 * 60 * 1000;
const MAX_MESSAGE = 16 * 1024;
const MAX_MESSAGES = 200;

export default {
  async fetch(request, env) {
    const url = new URL(request.url);
    const match = url.pathname.match(CODE);
    if (!match) return new Response("OptiGame", { status: 404 });
    if (request.headers.get("Upgrade") !== "websocket") return new Response("WebSocket attendu", { status: 426 });
    const role = url.searchParams.get("role");
    if (role !== "host" && role !== "guest") return new Response("Rôle inconnu", { status: 400 });
    return env.ROOMS.get(env.ROOMS.idFromName(match[1])).fetch(request);
  },
};

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
