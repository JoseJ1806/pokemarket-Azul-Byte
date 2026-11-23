<script setup>
// PANTALLA DE BIENVENIDA Y COLECCIÓN DEL JUGADOR (Sprint 2, US Recibir colección inicial).
//
// Qué muestra, según lo que pasó con la entrega en el último ingreso:
// - Granted:        "¡Bienvenido!" con las 15 cartas y los cubos que acaba de recibir.
// - AlreadyGranted: la colección del jugador, sin saludo especial.
// - NotAvailable:   el aviso de "juego no disponible por el momento". La cuenta existe,
//                   pero todavía no hay cartas para entregar: se completará en un ingreso
//                   posterior.
//
// Quién la ve: solo un jugador con sesión. Un administrador (token de desarrollo) o una
// persona sin sesión no tienen colección, y la API responde 401 o 404: en esos casos la
// pantalla simplemente no se muestra.
//
// Cómo decide qué mostrar: SIEMPRE le pregunta a la API (GET /api/me/collection) y solo usa
// "resultadoEntrega" para el saludo. Así funciona igual cuando la página se recarga con la
// sesión ya abierta (entonces no se conoce el resultado del login, pero la colección sí).

import { computed, onMounted, ref, watch } from "vue";
import { obtenerMiColeccion } from "../api/autenticacion.js";

const props = defineProps({
  // App.vue sube este número cada vez que cambia el token guardado (inicio o cierre de
  // sesión). Al cambiar, se vuelve a pedir la colección.
  versionToken: { type: Number, default: 0 },
  // Resultado de la entrega en el último inicio de sesión: "Granted", "AlreadyGranted",
  // "NotAvailable", o null si no se conoce (por ejemplo, al recargar la página).
  resultadoEntrega: { type: String, default: null },
});

// La colección que devolvió la API: { cubes, cards }. null = no hay sesión de jugador.
const coleccion = ref(null);
const error = ref("");

// Las cartas de la colección. Si la respuesta no trajera la lista (forma inesperada), se
// usa una lista vacía en vez de romper la pantalla.
const cartas = computed(() => coleccion.value?.cards ?? []);

// Total de copias (la API devuelve las cartas agrupadas, con su cantidad de copias).
const totalCopias = computed(() => cartas.value.reduce((suma, carta) => suma + carta.copies, 0));

async function cargar() {
  error.value = "";
  try {
    coleccion.value = await obtenerMiColeccion();
  } catch (e) {
    coleccion.value = null;
    // 401 (sin sesión) y 404 (token que no es de un jugador) son situaciones NORMALES:
    // simplemente no hay nada que mostrar. Cualquier otro error sí se avisa.
    if (e.estado !== 401 && e.estado !== 404) error.value = e.message;
  }
}

onMounted(cargar);
watch(() => props.versionToken, cargar);
</script>

<template>
  <section v-if="coleccion" class="tarjeta">
    <h2>Tu colección</h2>

    <!-- SALUDO: solo cuando la entrega ocurrió en este mismo ingreso. -->
    <p v-if="resultadoEntrega === 'Granted'" class="bienvenida">
      ¡Bienvenido a PokéArena! Recibiste {{ totalCopias }} cartas y
      {{ coleccion.cubes }} cubos para empezar.
    </p>

    <!-- SIN COLECCIÓN: la cuenta existe, pero todavía no se pudo entregar. -->
    <p v-if="totalCopias === 0" class="aviso">
      El juego todavía no está disponible por el momento. Vuelve a entrar más tarde:
      recibirás tu colección inicial cuando esté lista.
    </p>

    <template v-else>
      <p class="nota">
        Cubos: <strong>{{ coleccion.cubes }}</strong> · Cartas distintas:
        <strong>{{ cartas.length }}</strong> · Copias en total:
        <strong>{{ totalCopias }}</strong>
      </p>

      <!-- Una tarjeta por carta distinta. "copies" dice cuántas copias tiene. -->
      <ul class="cartas">
        <li v-for="carta in cartas" :key="carta.cardId" class="carta">
          <img v-if="carta.thumbnailUrl" :src="carta.thumbnailUrl" :alt="carta.name" />
          <div v-else class="sin-imagen">sin imagen</div>
          <strong>{{ carta.name }}</strong>
          <span class="nota">{{ carta.type }} · {{ carta.rarity }}</span>
          <span v-if="carta.copies > 1" class="copias">x{{ carta.copies }}</span>
        </li>
      </ul>
    </template>
  </section>

  <p v-else-if="error" class="mensaje-error">{{ error }}</p>
</template>

<style scoped>
/* Cuadrícula de cartas: tantas columnas como quepan, con un ancho mínimo por carta. */
.cartas {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(130px, 1fr));
  gap: 0.75rem;
  list-style: none;
  padding: 0;
  margin: 0;
}

.carta {
  position: relative;
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
  padding: 0.5rem;
  border: 1px solid var(--borde);
  border-radius: 8px;
  background: var(--fondo);
  text-align: center;
}

.carta img {
  width: 100%;
  border-radius: 6px;
}

.sin-imagen {
  padding: 2rem 0;
  color: var(--texto-suave);
}

/* Insignia con la cantidad de copias, solo cuando hay más de una. */
.copias {
  position: absolute;
  top: 0.4rem;
  right: 0.4rem;
  padding: 0 0.4rem;
  border-radius: 999px;
  background: var(--acento);
  color: var(--acento-texto);
  font-size: 0.8rem;
  font-weight: 600;
}

.bienvenida {
  font-weight: 600;
}

.aviso {
  padding: 0.75rem;
  border: 1px solid var(--borde);
  border-radius: 6px;
}
</style>
