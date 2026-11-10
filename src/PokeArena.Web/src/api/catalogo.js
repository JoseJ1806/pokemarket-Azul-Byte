import { pedir } from "./clienteApi.js";

export async function explorarCatalogo(filtros) {
  const parametros = new URLSearchParams();
  for (const [nombre, valor] of Object.entries(filtros)) {
    if (valor === "" || valor === null || valor === undefined) continue;
    parametros.set(nombre, String(valor));
  }
  const { cuerpo } = await pedir(`/api/v1/cards/catalog?${parametros.toString()}`);
  return cuerpo;
}
