import { pedir } from "./clienteApi.js";

export async function iniciarPartida(datos, idempotencyKey) {
  const { cuerpo } = await pedir("/api/v1/matches", {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      "Idempotency-Key": idempotencyKey,
    },
    body: JSON.stringify(datos),
  });
  return cuerpo;
}
