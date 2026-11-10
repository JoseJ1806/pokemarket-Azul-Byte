<script setup>
// PANTALLA DE INICIO DE SESIÓN CON GOOGLE (Sprint 2, US Inicio de sesión con proveedor).
//
// Es el participante "Cliente web (IniciarSesion.vue)" del diagrama de secuencia de V5.
// Hace tres cosas:
// 1. Dibuja el botón oficial de Google ("Iniciar sesión con Google").
// 2. Cuando la persona elige su cuenta, Google llama a alRecibirCredencial con un ID
//    token. Ese token se manda a la API, que responde con la credencial de PokéArena.
// 3. Guarda esa credencial con guardarToken (la MISMA función que usa el cuadro de
//    token de desarrollo) y muestra los datos del jugador.
//
// Por qué se usa el botón oficial de Google y no uno propio:
// Google Identity Services se encarga de la ventana de elegir cuenta, del
// consentimiento y de firmar el ID token. Nosotros solo recibimos el resultado.

import { onMounted, ref, watch } from "vue";
import { borrarToken, guardarToken } from "../api/clienteApi.js";
import { iniciarSesionConGoogle, obtenerJugadorActual } from "../api/autenticacion.js";

// App.vue sube este número cada vez que cambia el token guardado (por ejemplo, al
// pegar uno de administrador). Al cambiar, se vuelve a preguntar quién es el jugador.
const props = defineProps({ versionToken: { type: Number, default: 0 } });

// Avisos hacia App.vue para que actualice el resto de pantallas.
const emit = defineEmits(["sesion-iniciada", "sesion-cerrada"]);

// Client ID de Google. Vite lee las variables que empiezan con VITE_ desde .env.local
// (ver .env.example). Debe ser el mismo que Authentication:Google:ClientId de la API.
const CLIENT_ID = import.meta.env.VITE_GOOGLE_CLIENT_ID ?? "";

// Script oficial de Google Identity Services.
const URL_SCRIPT_GOOGLE = "https://accounts.google.com/gsi/client";

// Estado de la pantalla.
const jugador = ref(null); // datos del jugador con sesión, o null si no hay sesión
const esCuentaNueva = ref(false); // true justo después del primer ingreso
const error = ref(""); // mensaje de error para mostrar
const cargando = ref(false); // true mientras se espera la respuesta de la API
const contenedorBoton = ref(null); // el <div> donde Google dibuja su botón

// Carga el script de Google una sola vez. Devuelve una promesa que se cumple cuando
// el objeto window.google ya existe.
function cargarScriptGoogle() {
  if (window.google?.accounts?.id) return Promise.resolve();

  return new Promise((resolver, rechazar) => {
    const script = document.createElement("script");
    script.src = URL_SCRIPT_GOOGLE;
    script.async = true;
    script.onload = () => resolver();
    script.onerror = () => rechazar(new Error("No se pudo cargar el script de Google."));
    document.head.appendChild(script);
  });
}

// Dibuja el botón de Google dentro de contenedorBoton.
async function prepararBotonGoogle() {
  if (!CLIENT_ID) {
    error.value =
      "Falta configurar VITE_GOOGLE_CLIENT_ID en src/PokeArena.Web/.env.local (ver .env.example).";
    return;
  }

  try {
    await cargarScriptGoogle();
  } catch (e) {
    error.value = e.message;
    return;
  }

  // initialize le dice a Google cuál es nuestra aplicación y qué función llamar
  // cuando la persona termine de elegir su cuenta.
  window.google.accounts.id.initialize({
    client_id: CLIENT_ID,
    callback: alRecibirCredencial,
  });

  // renderButton dibuja el botón oficial. Solo se dibuja si el <div> está en pantalla.
  if (contenedorBoton.value) {
    window.google.accounts.id.renderButton(contenedorBoton.value, {
      theme: "outline",
      size: "large",
      text: "signin_with",
      locale: "es",
    });
  }
}

// Google llama a esta función con { credential: "<ID token>" }.
// Aquí empieza la parte del diagrama que va del cliente a la API.
async function alRecibirCredencial(respuestaGoogle) {
  error.value = "";
  cargando.value = true;
  try {
    // El ID token de Google se usa UNA sola vez, aquí. La API lo valida y devuelve
    // su propia credencial (RNF-SEG-01).
    const resultado = await iniciarSesionConGoogle(respuestaGoogle.credential);

    // Desde ahora, todas las llamadas a la API usan la credencial de PokéArena.
    guardarToken(resultado.accessToken);
    jugador.value = resultado.player;
    esCuentaNueva.value = resultado.isNewAccount;
    // Se avisa a App.vue con el resultado de la entrega de la colección inicial, para que
    // la pantalla de bienvenida sepa si debe saludar o mostrar el aviso de "no disponible".
    emit("sesion-iniciada", { initialCollection: resultado.initialCollection });
  } catch (e) {
    // ErrorApi ya trae el mensaje de la API (por ejemplo "El token de Google no es
    // válido" o "No se pudo contactar a Google").
    error.value = e.message;
  } finally {
    cargando.value = false;
  }
}

// Al abrir la página (o al cambiar el token), pregunta si el token guardado pertenece
// a un jugador. Si no hay token, si expiró o si es de administrador, simplemente no
// hay sesión de jugador y se muestra el botón.
async function revisarSesionGuardada() {
  try {
    jugador.value = await obtenerJugadorActual();
  } catch {
    jugador.value = null;
  }
}

// Botón "Salir". Olvida la credencial en este navegador.
// Nota: el cierre de sesión en el servidor es otra US (Renovación y cierre de sesión).
function salir() {
  borrarToken();
  jugador.value = null;
  esCuentaNueva.value = false;
  // Evita que Google vuelva a elegir la misma cuenta sola en el siguiente ingreso.
  window.google?.accounts?.id?.disableAutoSelect();
  emit("sesion-cerrada");
}

// Cuando el jugador sale, el <div> del botón vuelve a aparecer y hay que dibujarlo.
watch(jugador, async (actual) => {
  if (actual === null) {
    await Promise.resolve(); // espera a que Vue vuelva a poner el <div> en pantalla
    prepararBotonGoogle();
  }
});

// Si App.vue avisa que cambió el token, se vuelve a revisar la sesión.
watch(
  () => props.versionToken,
  () => revisarSesionGuardada(),
);

onMounted(async () => {
  await revisarSesionGuardada();
  if (jugador.value === null) prepararBotonGoogle();
});
</script>

<template>
  <div class="tarjeta">
    <h2>Sesión de jugador</h2>

    <!-- CON SESIÓN: datos del jugador y botón para salir. -->
    <div v-if="jugador">
      <p v-if="esCuentaNueva">¡Bienvenido a PokéArena! Tu cuenta se creó ahora mismo.</p>
      <p>
        Sesión iniciada como <strong>{{ jugador.email }}</strong>
        (rol {{ jugador.role }}, Elo {{ jugador.elo }}).
      </p>
      <button @click="salir">Salir</button>
    </div>

    <!-- SIN SESIÓN: aquí Google dibuja su botón. -->
    <div v-else>
      <div ref="contenedorBoton"></div>
      <p v-if="cargando">Validando con el servidor...</p>
    </div>

    <p v-if="error" class="mensaje-error">{{ error }}</p>
  </div>
</template>
