<script setup>
import { ref } from "vue";
import { guardarToken, obtenerToken } from "./api/clienteApi.js";
import BuscarCartas from "./components/BuscarCartas.vue";
import CrearCarta from "./components/CrearCarta.vue";
import CrearUbicacion from "./components/CrearUbicacion.vue";

// Las pantallas disponibles. 'id' identifica la pantalla en el código y 'etiqueta'
// es el texto de su pestaña. Agregar una pantalla nueva empieza por sumar una línea aquí.
const PANTALLAS = [
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
</script>

<template>
  <h1>PokéArena</h1>

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
  <section v-show="pantalla === 'carta'">
    <CrearCarta :version-token="versionToken" />
  </section>
  <section v-show="pantalla === 'ubicacion'">
    <CrearUbicacion />
  </section>
</template>
