const CLAVE_TOKEN = "token-dev";

export function guardarToken(token) {
  localStorage.setItem(CLAVE_TOKEN, token);
}

// Agregada en el Sprint 2 para el botón "Salir" de IniciarSesion.vue.
// Solo olvida el token en ESTE navegador. El token sigue siendo válido hasta que
// expire (15 minutos). Invalidarlo en el servidor es parte de la US de Renovación y
// cierre de sesión, que todavía no se implementa.
export function borrarToken() {
  localStorage.removeItem(CLAVE_TOKEN);
}

// Mensajes comprensibles para los códigos que la API o el proxy devuelven sin
// explicación en el cuerpo. Solo se usan cuando la respuesta no trae un texto
// propio: si la API explica el problema, ese texto manda.
const MENSAJES_POR_ESTADO = {
  401: "No estás autenticado. Guarda un token válido arriba.",
  403: "Tu usuario no tiene permiso de administrador.",
  502: "No se pudo conectar con la API. Revisa que esté corriendo.",
  503: "La API no está disponible en este momento.",
};

export class ErrorApi extends Error {
  constructor(estado, problema) {
    // El mensaje sale del primer dato disponible, en este orden:
    // 1. detail: el formato de errores de Crear carta (problem+json)
    // 2. error: el formato que usa hoy Buscar carta
    // 3. title: el título genérico que ASP.NET agrega a algunos errores
    // 4. un mensaje nuestro según el código, y 5. el código a secas como último recurso
    super(
      problema?.detail ??
        problema?.error ??
        problema?.title ??
        MENSAJES_POR_ESTADO[estado] ??
        `Error ${estado}`,
    );
    this.estado = estado;
    this.codigo = problema?.code;
    this.campo = problema?.field;
  }
}

export async function pedir(ruta, opciones = {}) {
  const cabeceras = { ...opciones.headers };
  const token = localStorage.getItem(CLAVE_TOKEN);
  if (token) cabeceras.Authorization = `Bearer ${token}`;

  // fetch lanza una excepción cuando ni siquiera hay conexión (servidor apagado,
  // sin red). En ese caso no existe un código HTTP, así que usamos el 0 para
  // identificar "no se pudo ni llegar al servidor".
  let respuesta;
  try {
    respuesta = await fetch(ruta, { ...opciones, headers: cabeceras });
  } catch {
    throw new ErrorApi(0, { detail: "No hay conexión con el servidor." });
  }

  const esJson = respuesta.headers.get("content-type")?.includes("json");
  const cuerpo = esJson ? await respuesta.json() : null;

  if (!respuesta.ok) throw new ErrorApi(respuesta.status, cuerpo);
  return { estado: respuesta.status, cuerpo };
}

// Devuelve el token que se guardó antes, o un texto vacío si no hay ninguno.
// Existe para que la pantalla pueda mostrar el token guardado al recargar,
// sin que otros archivos tengan que conocer la clave 'token-dev' de localStorage.
// Esa clave solo se conoce aquí, en el cliente de la API.
export function obtenerToken() {
  return localStorage.getItem(CLAVE_TOKEN) ?? "";
}
