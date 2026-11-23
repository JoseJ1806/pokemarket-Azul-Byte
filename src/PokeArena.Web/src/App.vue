<script setup>
import { ref } from "vue";
import { guardarToken, obtenerToken } from "./api/clienteApi.js";
import BuscarCartas from "./components/BuscarCartas.vue";
import CatalogoJugador from "./components/CatalogoJugador.vue";
import CrearCarta from "./components/CrearCarta.vue";
import CrearUbicacion from "./components/CrearUbicacion.vue";
import Bienvenida from "./components/Bienvenida.vue";
import IniciarSesion from "./components/IniciarSesion.vue";

// Las pantallas disponibles. 'id' identifica la pantalla en el código y 'etiqueta'
// es el texto de su pestaña. Agregar una pantalla nueva empieza por sumar una línea aquí.
const PANTALLAS = [
  { id: "catalogo", etiqueta: "Catálogo" },
  { id: "buscar", etiqueta: "Buscar cartas" },
  { id: "carta", etiqueta: "Crear carta" },
  { id: "ubicacion", etiqueta: "Crear ubicación" },
];

// La pantalla que se está viendo ahora.
const pantalla = ref("buscar");

// El token que el usuario escribe, y el aviso que confirma que se guardó.
const token = ref(obtenerToken());
const aviso = ref("");

// Un contador que sube cada vez que se guarda un token.
// Las pantallas lo reciben y lo VIGILAN: cuando cambia, saben que el token nuevo ya
// está disponible y vuelven a pedir lo que habían pedido con el token anterior (por
// ejemplo, la lista de habilidades). Así nadie tiene que recargar la página.
const versionToken = ref(0);

function aplicarToken() {
  const limpio = token.value.trim();
  if (limpio === "") {
    aviso.value = "Pega primero un token.";
    return;
  }
  guardarToken(limpio);
  versionToken.value++;
  aviso.value = `Token guardado a las ${new Date().toLocaleTimeString()}.`;
}

// Sprint 2: IniciarSesion.vue avisa cuando un jugador entra o sale con Google.
// En los dos casos el token guardado cambió, así que se refleja en el cuadro de token
// de desarrollo y se sube versionToken para que las demás pantallas se enteren.
function alCambiarSesion() {
  token.value = obtenerToken();
  versionToken.value++;
}

// Qué pasó con la colección inicial en el último inicio de sesión ("Granted",
// "AlreadyGranted", "NotAvailable"), o null si no se conoce (sin sesión, o página recargada).
// Lo usa la pantalla de bienvenida para decidir si saluda.
const resultadoEntrega = ref(null);

// IniciarSesion avisa que un jugador entró y trae el resultado de la entrega.
function alIniciarSesion(datos) {
  resultadoEntrega.value = datos?.initialCollection ?? null;
  alCambiarSesion();
}

// IniciarSesion avisa que el jugador salió: se olvida el resultado.
function alCerrarSesion() {
  resultadoEntrega.value = null;
  alCambiarSesion();
}
</script>

<template>
  <h1>PokéArena</h1>

  <!-- Sprint 2: inicio de sesión con Google para jugadores. El cuadro de token de
       desarrollo de abajo se mantiene para los administradores, que todavía no
       tienen forma de entrar con Google. -->
  <IniciarSesion
    :version-token="versionToken"
    @sesion-iniciada="alIniciarSesion"
    @sesion-cerrada="alCerrarSesion"
  />

  <!-- Sprint 2: bienvenida y colección del jugador. Solo se dibuja si hay sesión de jugador;
       para un administrador o sin sesión, no muestra nada. -->
  <Bienvenida :version-token="versionToken" :resultado-entrega="resultadoEntrega" />

  <details class="tarjeta">
    <summary>Token de desarrollo</summary>
    <label>
      Pega aquí el token
      <textarea v-model="token" rows="3"></textarea>
    </label>
    <p>
      <button class="primario" @click="aplicarToken">Guardar token</button>
    </p>
    <p v-if="aviso">{{ aviso }}</p>
  </details>

  <!-- Las pestañas. :class marca como "activa" la pestaña de la pantalla actual. -->
  <nav class="pestanas">
    <button
      v-for="p in PANTALLAS"
      :key="p.id"
      :class="{ activa: pantalla === p.id }"
      @click="pantalla = p.id"
    >
      {{ p.etiqueta }}
    </button>
  </nav>

  <!-- v-show ESCONDE la pantalla sin destruirla. Con v-if, al cambiar de pestaña se
       perdería lo escrito en el formulario y los resultados de la búsqueda. -->
  <section v-show="pantalla === 'buscar'">
    <BuscarCartas />
  </section>
  <section v-show="pantalla === 'catalogo'">
    <CatalogoJugador :version-token="versionToken" />
  </section>
  <section v-show="pantalla === 'carta'">
    <CrearCarta :version-token="versionToken" />
  </section>
  <section v-show="pantalla === 'ubicacion'">
    <CrearUbicacion />
  </section>
</template>
