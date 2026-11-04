<script setup>
// ref crea valores "reactivos": cuando cambian, Vue redibuja lo que los muestre.
import { ref } from "vue";

// Estas dos funciones viven en el cliente de la API. App.vue no sabe cómo ni dónde
// se guarda el token: solo pide guardarlo u obtenerlo.
import { guardarToken, obtenerToken } from "./api/clienteApi.js";

// CrearCarta es una pantalla completa que vive en su propio archivo.
import CrearCarta from "./components/CrearCarta.vue";

// Pantalla de búsqueda, que vive en su propio archivo.
import BuscarCartas from "./components/BuscarCartas.vue";

// Pantalla de Crear ubicación.
import CrearUbicacion from "./components/CrearUbicacion.vue";

// Lo que el usuario escribe en el cuadro de texto. Arranca con el token que ya
// estaba guardado, para que al recargar la página no se vea vacío.
const token = ref(obtenerToken());

// Texto de confirmación que se muestra debajo del botón. Vacío = no se muestra.
const aviso = ref("");

function aplicarToken() {
  // trim() quita espacios y saltos de línea al inicio y al final. Es útil porque
  // al copiar un token es muy común que se cuele un espacio o un salto de línea,
  // y con uno solo la API respondería 401 sin que se note el motivo.
  const limpio = token.value.trim();

  // Un token vacío no sirve para nada: se avisa en vez de guardar en silencio.
  if (limpio === "") {
    aviso.value = "Pega primero un token.";
    return;
  }

  guardarToken(limpio);

  // toLocaleTimeString da la hora local, para que se note que la acción ocurrió
  // de nuevo si presionas el botón varias veces.
  aviso.value =
    `Token guardado a las ${new Date().toLocaleTimeString()}. ` +
    "Si la lista de habilidades salió vacía, recarga la página.";
}
</script>

<template>
  <h1>PokéArena</h1>

  <!-- <details> es un bloque que se puede abrir y cerrar. 'open' lo deja abierto al inicio. -->
  <details open>
    <summary>Token de desarrollo</summary>

    <!-- v-model une el cuadro con la variable 'token': lo que escribes se guarda
         en ella, y si la variable cambia, el cuadro se actualiza. -->
    <textarea
      v-model="token"
      rows="3"
      cols="60"
      placeholder="Pega aquí el token"
    ></textarea>
    <br />

    <!-- @click ejecuta la función al presionar el botón. -->
    <button @click="aplicarToken">Guardar token</button>

    <!-- v-if solo dibuja este párrafo si 'aviso' no está vacío. -->
    <p v-if="aviso">{{ aviso }}</p>
  </details>

  <hr />

  <!-- Aquí se dibuja la pantalla de Crear carta y Buscar cartas -->
  <CrearCarta />
  <hr />
  <CrearUbicacion />
  <hr />
  <BuscarCartas />
</template>
