// LLAMADAS A LA API DE IDENTIDAD (Sprint 2, Inicio de sesión con proveedor).
//
// Igual que cartas.js y ubicaciones.js: cada función es una llamada a un endpoint.
// Todas pasan por pedir() de clienteApi.js, que agrega el token guardado y convierte
// los errores ProblemDetails en un ErrorApi con mensaje legible.

import { pedir } from "./clienteApi.js";

// POST /api/auth/google
// Recibe el ID token que entregó el botón de Google y lo cambia por la credencial
// propia de PokéArena. Es el paso "iniciarSesionConGoogle(idToken)" del diagrama de
// secuencia de V5.
// Devuelve { accessToken, expiresAt, player, isNewAccount, initialCollection }.
// initialCollection dice qué pasó con la colección inicial en ESTE ingreso, como texto:
// "Granted" (se entregó ahora), "AlreadyGranted" (ya la tenía) o "NotAvailable" (todavía no
// se puede entregar: hay menos de 15 Comunes disponibles).
export async function iniciarSesionConGoogle(idToken) {
  const { cuerpo } = await pedir("/api/auth/google", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ idToken }),
  });
  return cuerpo;
}

// GET /api/me
// Devuelve la cuenta dueña del token guardado: { id, email, role, elo, createdAt }.
// Se usa al cargar la página para saber si ya había una sesión de jugador abierta.
export async function obtenerJugadorActual() {
  const { cuerpo } = await pedir("/api/me");
  return cuerpo;
}

// GET /api/me/collection
// Devuelve la colección del jugador dueño del token, agrupada por carta:
// { cubes, cards: [{ cardId, code, name, type, rarity, imageUrl, thumbnailUrl, copies }] }.
// Responde 401 si no hay sesión y 404 si el token no es de un jugador (por ejemplo, el de
// administrador de desarrollo): quien llama debe tratarlos como "no hay colección".
export async function obtenerMiColeccion() {
  const { cuerpo } = await pedir("/api/me/collection");
  return cuerpo;
}
