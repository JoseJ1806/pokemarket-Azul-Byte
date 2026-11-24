// LLAMADAS A LA API DE DECKS (Sprint 3, US Crear Deck).
//
// Igual que autenticacion.js y cartas.js: cada función es una llamada a un endpoint y todas
// pasan por pedir() de clienteApi.js, que agrega el token y convierte los errores
// ProblemDetails en un ErrorApi con mensaje legible. El jugador NUNCA se envía: la API lo
// saca del token, así que no se puede crear ni ver el deck de otra persona.

import { pedir } from "./clienteApi.js";

// POST /api/me/decks
// Crea un deck con un nombre y exactamente 12 cartas distintas de la colección del jugador.
// 'llave' es la Idempotency-Key: si la misma solicitud se reenvía por un reintento de red,
// la API devuelve el mismo deck en lugar de crear otro (200 en vez de 201).
// Devuelve { id, name, cardCount, cardIds, createdAt }.
// Los errores de la regla llegan en ErrorApi con 'codigo' y 'campo', por ejemplo:
// invalid-deck-size, duplicate-cards, card-not-owned, deck-name-taken.
export async function crearDeck(nombre, cardIds, llave) {
  const { cuerpo } = await pedir("/api/me/decks", {
    method: "POST",
    headers: { "Content-Type": "application/json", "Idempotency-Key": llave },
    body: JSON.stringify({ name: nombre, cardIds }),
  });
  return cuerpo;
}

// GET /api/me/decks
// Los decks del jugador, del más antiguo al más reciente:
// [{ id, name, cardCount, cardIds, createdAt }].
export async function listarMisDecks() {
  const { cuerpo } = await pedir("/api/me/decks");
  return cuerpo;
}

// GET /api/me/decks/{id}
// Un deck con sus cartas ya presentables:
// { id, name, createdAt, cards: [{ cardId, code, name, type, rarity, imageUrl, thumbnailUrl }] }.
// Responde 404 si el deck no existe o es de otro jugador (la API no distingue los dos casos).
export async function obtenerDeck(id) {
  const { cuerpo } = await pedir(`/api/me/decks/${id}`);
  return cuerpo;
}
