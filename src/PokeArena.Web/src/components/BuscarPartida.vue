<script setup>
// PANTALLA DE LA FILA DE BÚSQUEDA (Sprint 3, US Buscar partida y Consultar y salir de la
// búsqueda).
//
// Qué hace: el jugador elige uno de sus decks y busca partida. Mientras espera, la pantalla
// consulta el estado cada pocos segundos y muestra el tiempo de espera y el rango de Elo que
// se acepta en ese momento. Puede salir de la búsqueda. Cuando se encuentra rival, muestra la
// partida asignada.
//
// Dónde están las reglas: todas en la API (deck válido, 8 cubos, una sola búsqueda abierta,
// partida activa). Aquí solo se muestra el resultado y, si la API rechaza algo, su mensaje.
//
// Por qué se consulta cada pocos segundos: la API saca de la búsqueda a quien deja de
// consultarla (#128). Esta pantalla se esconde con v-show al cambiar de pestaña, así que sigue
// consultando aunque el jugador mire otra pestaña.

import { computed, onMounted, onUnmounted, ref, watch } from "vue";
import { listarMisDecks } from "../api/decks.js";
import {
  buscarPartida,
  consultarBusqueda,
  salirDeBusqueda,
} from "../api/emparejamiento.js";

const props = defineProps({
  // App.vue sube este número cada vez que cambia el token guardado (inicio o cierre de
  // sesión). Al cambiar, se vuelve a pedir todo.
  versionToken: { type: Number, default: 0 },
});

const decks = ref([]);
const deckId = ref("");
const busqueda = ref(null); // la búsqueda más reciente tal como la devuelve la API
const haySesion = ref(true);
const enviando = ref(false);
const error = ref("");
let temporizador = null;

const enEspera = computed(() => busqueda.value?.status === "Waiting");
const nombreDeckBuscado = computed(
  () => decks.value.find((d) => d.id === busqueda.value?.deckId)?.name ?? "",
);
const puedeBuscar = computed(() => deckId.value !== "" && !enEspera.value && !enviando.value);

// Texto de cada estado del modelo de la V3.
const ESTADOS = {
  Waiting: "En espera",
  MatchFound: "Partida encontrada",
  Cancelled: "Cancelada",
  Expired: "Expirada",
};

const textoEstado = computed(() => {
  const b = busqueda.value;
  if (!b) return "";
  if (b.status === "Waiting" && b.creatingMatch) return "Rival encontrado: creando la partida...";
  return ESTADOS[b.status] ?? b.status;
});

// Consulta periódica mientras la búsqueda está en espera.
function detenerConsultas() {
  if (temporizador) clearInterval(temporizador);
  temporizador = null;
}

function iniciarConsultas() {
  detenerConsultas();
  const segundos = busqueda.value?.pollIntervalSeconds ?? 2;
  temporizador = setInterval(consultar, segundos * 1000);
}

function mostrar(nueva) {
  busqueda.value = nueva;
  if (nueva?.status === "Waiting") {
    // La lista muestra el deck con el que se está buscando.
    deckId.value = nueva.deckId;
    if (!temporizador) iniciarConsultas();
  } else {
    detenerConsultas();
  }
}

async function consultar() {
  try {
    mostrar(await consultarBusqueda());
  } catch (e) {
    // Un fallo de red no cancela la búsqueda: se vuelve a intentar en la próxima consulta.
    error.value = e.message;
  }
}

async function cargar() {
  detenerConsultas();
  error.value = "";
  try {
    decks.value = await listarMisDecks();
    haySesion.value = true;
    if (deckId.value === "" && decks.value.length > 0) deckId.value = decks.value[0].id;
    // Si el jugador ya tenía una búsqueda (por ejemplo, recargó la página), se retoma.
    mostrar(await consultarBusqueda());
  } catch (e) {
    decks.value = [];
    busqueda.value = null;
    // 401 (sin sesión) y 404 (token que no es de un jugador) son situaciones normales.
    haySesion.value = e.estado !== 401 && e.estado !== 404;
    if (haySesion.value) error.value = e.message;
  }
}

async function buscar() {
  enviando.value = true;
  error.value = "";
  try {
    mostrar(await buscarPartida(deckId.value));
  } catch (e) {
    error.value = e.message;
  } finally {
    enviando.value = false;
  }
}

async function salir() {
  enviando.value = true;
  error.value = "";
  try {
    mostrar(await salirDeBusqueda());
  } catch (e) {
    // Por ejemplo, ya se encontró partida: la API dice cuál. Se refresca el estado.
    error.value = e.message;
    await consultar();
  } finally {
    enviando.value = false;
  }
}

onMounted(cargar);
onUnmounted(detenerConsultas);
watch(() => props.versionToken, cargar);
</script>

<template>
  <section v-if="!haySesion" class="tarjeta">
    <h2>Buscar partida</h2>
    <p class="nota">Inicia sesión con Google como jugador para buscar partida.</p>
  </section>

  <section v-else class="tarjeta">
    <h2>Buscar partida</h2>

    <p v-if="decks.length === 0" class="nota">
      Necesitas un deck de 12 cartas para buscar partida. Ármalo en la pestaña "Armar deck".
    </p>

    <form v-else @submit.prevent="buscar">
      <label>
        Deck
        <!-- El deck no se puede cambiar mientras la búsqueda está abierta (#127). -->
        <select v-model="deckId" :disabled="enEspera">
          <option v-for="deck in decks" :key="deck.id" :value="deck.id">
            {{ deck.name }} ({{ deck.cardCount }} cartas)
          </option>
        </select>
      </label>

      <p>
        <button type="submit" class="primario" :disabled="!puedeBuscar">
          {{ enviando && !enEspera ? "Buscando..." : "Buscar partida" }}
        </button>
        <button
          v-if="enEspera"
          type="button"
          class="salir"
          :disabled="enviando || busqueda.creatingMatch"
          @click="salir"
        >
          Salir de la búsqueda
        </button>
      </p>
    </form>

    <p v-if="error" class="mensaje-error">{{ error }}</p>

    <div v-if="busqueda" class="estado" aria-live="polite">
      <p>
        <strong>Estado:</strong> {{ textoEstado }}
        <span v-if="nombreDeckBuscado" class="nota"> · deck "{{ nombreDeckBuscado }}"</span>
      </p>

      <template v-if="enEspera">
        <p>
          <strong>Tiempo de espera:</strong> {{ busqueda.waitedSeconds }} s ·
          <strong>Rango de Elo aceptado:</strong> ±{{ busqueda.eloRange }}
        </p>
        <p class="nota">
          El rango se amplía mientras esperas. Mantén esta página abierta: si dejas de consultar
          durante {{ busqueda.inactivityTimeoutSeconds }} segundos, sales de la búsqueda.
        </p>
      </template>

      <p v-if="busqueda.message" class="nota">{{ busqueda.message }}</p>

      <!-- RN-17: a los 60 segundos sin rival se ofrece el bot. El bot (#140) todavía no
           existe, así que la oferta se muestra pero no se puede aceptar. -->
      <div v-if="busqueda.botOfferAvailable" class="oferta-bot">
        <p>Llevas más de 60 segundos sin rival. Puedes seguir esperando o jugar contra el bot.</p>
        <button type="button" disabled>Jugar contra el bot</button>
        <p class="nota">Jugar contra el bot estará disponible con la funcionalidad del bot.</p>
      </div>

      <div v-if="busqueda.status === 'MatchFound'" class="tarjeta">
        <h3>¡Partida encontrada!</h3>
        <p><strong>Partida:</strong> {{ busqueda.matchId }}</p>
      </div>

      <p v-if="busqueda.status === 'Cancelled'" class="nota">
        Saliste de la búsqueda. Puedes buscar de nuevo cuando quieras.
      </p>
      <p v-if="busqueda.status === 'Expired'" class="nota">
        La búsqueda expiró porque dejaste de consultarla. Puedes buscar de nuevo.
      </p>
    </div>
  </section>
</template>

<style scoped>
.estado {
  margin-top: 1rem;
  padding-top: 0.5rem;
  border-top: 1px solid var(--borde);
}

.salir {
  margin-left: 0.75rem;
}

.oferta-bot {
  margin: 0.75rem 0;
  padding: 0.75rem;
  border: 1px dashed var(--borde);
  border-radius: 6px;
}
</style>
