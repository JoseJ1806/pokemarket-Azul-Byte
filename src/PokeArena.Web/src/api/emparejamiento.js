// LLAMADAS A LA API DEL EMPAREJAMIENTO (Sprint 3, US Buscar partida y Consultar y salir de
// la búsqueda).
//
// Igual que decks.js: cada función es una llamada a un endpoint y todas pasan por pedir() de
// clienteApi.js, que agrega el token y convierte los errores ProblemDetails en un ErrorApi con
// mensaje legible. El jugador NUNCA se envía: la API lo saca del token.
//
// Todas devuelven la búsqueda con esta forma:
// { searchId, status, context, deckId, matchId, creatingMatch, waitedSeconds, eloRange,
//   botOfferAvailable, message, pollIntervalSeconds, inactivityTimeoutSeconds, createdAt }
// status es "Waiting", "MatchFound", "Cancelled" o "Expired".

import { pedir } from "./clienteApi.js";

// POST /api/me/match-search
// Busca partida clasificatoria con uno de los decks del jugador. Si ya había una búsqueda
// abierta con ese mismo deck, la API devuelve esa misma búsqueda (doble clic o reintento).
// Errores con 'codigo': deck-required, deck-not-found, invalid-deck, insufficient-cubes,
// active-match, search-already-open.
export async function buscarPartida(deckId) {
  const { cuerpo } = await pedir("/api/me/match-search", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ deckId }),
  });
  return cuerpo;
}

// GET /api/me/match-search
// Estado de la búsqueda más reciente. Mientras está en espera hay que llamarla cada
// pollIntervalSeconds: si el jugador deja de consultar, queda fuera de la búsqueda.
// Devuelve null si el jugador nunca buscó partida (404 search-not-found).
export async function consultarBusqueda() {
  try {
    const { cuerpo } = await pedir("/api/me/match-search");
    return cuerpo;
  } catch (e) {
    if (e.estado === 404 && e.codigo === "search-not-found") return null;
    throw e;
  }
}

// DELETE /api/me/match-search
// Sale de la búsqueda abierta. Si ya se encontró partida, la API lo rechaza (409) e indica
// la partida asignada.
export async function salirDeBusqueda() {
  const { cuerpo } = await pedir("/api/me/match-search", { method: "DELETE" });
  return cuerpo;
}
