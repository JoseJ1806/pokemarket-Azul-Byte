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
// Devuelve { accessToken, expiresAt, player, isNewAccount }.
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
