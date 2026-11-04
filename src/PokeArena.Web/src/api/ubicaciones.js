import { pedir } from "./clienteApi.js";

// Crea una ubicación. Igual que crearCarta, este es el único lugar que conoce el
// formato que espera la API para ubicaciones: las pantallas no tocan FormData.
//
// datos.parametrosEfecto es un OBJETO de JavaScript (o null si no hay efecto).
// La API lo espera como TEXTO JSON dentro de un campo del formulario, porque un
// multipart solo transporta pares "nombre = texto"; no puede llevar objetos anidados.
export async function crearUbicacion(datos, imagen, llaveIdempotencia) {
  const formulario = new FormData();
  formulario.append("Name", datos.nombre);
  formulario.append("Description", datos.descripcion);
  formulario.append("CardLimit", datos.limiteCartas);
  formulario.append("EnergyMode", datos.modalidadEnergia);
  formulario.append("EnergyAmount", datos.cantidadEnergia);

  // Una ubicación puede no tener efecto. En ese caso NO se envían estos dos campos,
  // en vez de enviarlos vacíos: así la API los ve como "no enviado".
  if (datos.tipoEfecto) {
    formulario.append("EffectType", datos.tipoEfecto);
    // JSON.stringify convierte el objeto en texto: { cantidad: 2 } pasa a '{"cantidad":2}'.
    formulario.append(
      "EffectParameters",
      JSON.stringify(datos.parametrosEfecto),
    );
  }

  // La imagen puede faltar: la API responderá con el error que corresponde.
  if (imagen) formulario.append("Image", imagen);

  return pedir("/api/locations", {
    method: "POST",
    body: formulario,
    headers: { "Idempotency-Key": llaveIdempotencia },
  });
}
