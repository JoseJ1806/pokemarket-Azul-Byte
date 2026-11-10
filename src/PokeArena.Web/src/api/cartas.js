import { pedir } from "./clienteApi.js";

export async function listarHabilidades() {
  const { cuerpo } = await pedir("/api/abilities");
  return cuerpo;
}

export async function crearCarta(datos, imagen, llaveIdempotencia) {
  const formulario = new FormData();
  formulario.append("Name", datos.nombre);
  formulario.append("Cost", datos.costo);
  formulario.append("Power", datos.poder);
  formulario.append("Type", datos.tipo);
  formulario.append("Rarity", datos.rareza);
  formulario.append("AbilityCode", datos.codigoHabilidad);
  formulario.append("Image", imagen);

  return pedir("/api/cards", {
    method: "POST",
    body: formulario,
    headers: { "Idempotency-Key": llaveIdempotencia },
  });
}

// Busca cartas en la API.
// 'filtros' es un objeto cuyas llaves tienen EXACTAMENTE los nombres de los
// parámetros de la API (q, type, minCost...). Así la función los convierte sin
// tener que conocer cada uno: si la API suma un filtro, aquí no hay que tocar nada.
// 'cursor' es el texto que devolvió la página anterior (nextCursor), o null en la
// primera búsqueda.
export async function buscarCartas(filtros, cursor) {
  // URLSearchParams arma la parte de la URL que va después del "?" y se
  // encarga de codificar los caracteres especiales (espacios, tildes, etc.).
  const parametros = new URLSearchParams();

  for (const [nombre, valor] of Object.entries(filtros)) {
    // Un filtro vacío no se manda: si enviáramos "type=", sería un filtro con
    // texto vacío, que no es lo mismo que no filtrar.
    if (valor === "" || valor === null || valor === undefined) continue;
    parametros.append(nombre, valor);
  }

  // El cursor le dice a la API desde dónde seguir recorriendo los resultados.
  if (cursor) parametros.append("cursor", cursor);

  const { cuerpo } = await pedir(`/api/cards?${parametros.toString()}`);
  return cuerpo;
}
