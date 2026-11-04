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
const contenido = {
  sub: "admin-dev",
  role: "Admin",
  iat: ahora,
  exp: ahora + 60 * 60 * 8,
};

const parteFirmada = `${base64url(encabezado)}.${base64url(contenido)}`;
const firma = createHmac("sha256", clave)
  .update(parteFirmada)
  .digest("base64url");

console.log(`${parteFirmada}.${firma}`);
