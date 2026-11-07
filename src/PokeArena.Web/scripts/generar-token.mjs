import { createHmac } from "node:crypto";

const clave = process.env.Jwt__SigningKey;
if (!clave) {
  console.error("Falta definir la variable Jwt__SigningKey");
  process.exit(1);
}

const base64url = (objeto) =>
  Buffer.from(JSON.stringify(objeto)).toString("base64url");

const ahora = Math.floor(Date.now() / 1000);
const encabezado = { alg: "HS256", typ: "JWT" };
// Desde el Sprint 2 la API exige emisor (iss) y audiencia (aud) en todo token.
// Deben coincidir con Jwt:Issuer y Jwt:Audience de appsettings.json. Si no vienen,
// la API responde 401 aunque la firma sea correcta.
const contenido = {
  sub: "admin-dev",
  role: "Admin",
  iss: process.env.Jwt__Issuer ?? "pokearena",
  aud: process.env.Jwt__Audience ?? "pokearena-api",
  iat: ahora,
  exp: ahora + 60 * 60 * 8,
};

const parteFirmada = `${base64url(encabezado)}.${base64url(contenido)}`;
const firma = createHmac("sha256", clave)
  .update(parteFirmada)
  .digest("base64url");

console.log(`${parteFirmada}.${firma}`);
