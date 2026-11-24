<script setup>
// PANTALLA ARMAR DECK (Sprint 3, US Crear Deck).
//
// Qué hace: el jugador elige EXACTAMENTE 12 cartas distintas de su colección, le pone un
// nombre y lo guarda. También muestra los decks que ya tiene.
//
// Dónde están las reglas: aquí solo se ayuda al jugador (contador, botón deshabilitado hasta
// tener 12 cartas y nombre). Las reglas de verdad las valida la API: cantidad exacta, cartas
// repetidas, cartas que no posee y nombre repetido. Si la API rechaza algo, se muestra su
// mensaje tal cual.
//
// Reintentos de red: cada intento de guardar lleva una Idempotency-Key. Si la misma solicitud
// se reenvía, la API devuelve el mismo deck en vez de crear otro. La llave cambia cuando el
// jugador modifica el nombre o las cartas, porque entonces ya es otra solicitud.

import { computed, onMounted, ref, watch } from "vue";
import { obtenerMiColeccion } from "../api/autenticacion.js";
import { crearDeck, listarMisDecks, obtenerDeck } from "../api/decks.js";
import CampoFormulario from "./CampoFormulario.vue";
import CartaVista from "./CartaVista.vue";

const props = defineProps({
  // App.vue sube este número cada vez que cambia el token guardado (inicio o cierre de
  // sesión). Al cambiar, se vuelve a pedir la colección y los decks.
  versionToken: { type: Number, default: 0 },
});

// Cantidad de cartas de un deck. Es la misma regla que DeckRules.Size en la API.
const TAMANO_DECK = 12;

const cartas = ref([]); // la colección del jugador (una entrada por carta distinta)
const decks = ref([]); // los decks que ya tiene
const haySesion = ref(true); // false cuando no hay un jugador con sesión (401 o 404)
const deckAbierto = ref(null); // el deck cuyas cartas se están viendo (detalle completo de la API)
let idPedido = null; // último deck pedido (para descartar respuestas viejas)
const cargandoDeck = ref(false);
const errorDeck = ref("");
const seleccion = ref([]); // ids de las cartas elegidas, en el orden en que se eligieron
const nombre = ref("");
const llave = ref(nuevaLlave());
const enviando = ref(false);
const error = ref("");
const aviso = ref("");

function nuevaLlave() {
  return crypto.randomUUID();
}

const cantidad = computed(() => seleccion.value.length);
const completo = computed(() => cantidad.value === TAMANO_DECK);
const puedeGuardar = computed(
  () => completo.value && nombre.value.trim() !== "" && !enviando.value,
);

function estaElegida(cardId) {
  return seleccion.value.includes(cardId);
}

// Agrega la carta si no estaba y hay lugar; si ya estaba, la quita.
function alternar(cardId) {
  if (estaElegida(cardId)) {
    seleccion.value = seleccion.value.filter((id) => id !== cardId);
  } else if (cantidad.value < TAMANO_DECK) {
    seleccion.value = [...seleccion.value, cardId];
  }
}

// Cualquier cambio del nombre o de las cartas es otra solicitud: llave nueva y sin avisos
// viejos. Si el jugador solo reintenta (sin tocar nada), la llave se conserva.
watch([nombre, seleccion], () => {
  llave.value = nuevaLlave();
  error.value = "";
  aviso.value = "";
});

// Texto de la habilidad de una carta, o "" si no tiene. La API manda el código y sus partes
// (cuándo se activa, qué efecto tiene y a qué tipo afecta). "HAB-000" es "sin habilidad".
function textoHabilidad(carta) {
  if (!carta.abilityCode || carta.abilityCode === "HAB-000") return "";
  return [carta.abilityCode, carta.abilityTrigger, carta.abilityEffect, carta.abilityAffectedType]
    .filter(Boolean)
    .join(" · ");
}

// Abre o cierra las cartas de un deck. Un segundo clic sobre el mismo deck lo cierra.
// El detalle se pide a GET /api/me/decks/{id}: la lista no trae las cartas, solo sus ids.
async function verDeck(id) {
  errorDeck.value = "";
  if (deckAbierto.value?.id === id) {
    deckAbierto.value = null;
    return;
  }
  cargandoDeck.value = true;
  deckAbierto.value = null;
  // Si el jugador hace clic en otro deck mientras este carga, se ignora la respuesta vieja.
  idPedido = id;
  try {
    const detalle = await obtenerDeck(id);
    if (idPedido === id) deckAbierto.value = detalle;
  } catch (e) {
    if (idPedido === id) errorDeck.value = e.message;
  } finally {
    if (idPedido === id) cargandoDeck.value = false;
  }
}
async function cargar() {
  try {
    cartas.value = (await obtenerMiColeccion()).cards ?? [];
    decks.value = await listarMisDecks();
    haySesion.value = true;
  } catch (e) {
    cartas.value = [];
    decks.value = [];
    // 401 (sin sesión) y 404 (token que no es de un jugador) son situaciones normales.
    haySesion.value = e.estado !== 401 && e.estado !== 404;
    if (haySesion.value) error.value = e.message;
  }
}

async function guardar() {
  enviando.value = true;
  error.value = "";
  aviso.value = "";
  try {
    const deck = await crearDeck(nombre.value.trim(), seleccion.value, llave.value);
    // Se vacía el formulario. Eso dispara el watch de arriba (llave nueva y avisos borrados),
    // que corre mientras se espera la lista de decks. Por eso el aviso se escribe DESPUÉS
    // de esa espera: si se escribiera antes, el watch lo borraría.
    nombre.value = "";
    seleccion.value = [];
    decks.value = await listarMisDecks();
    aviso.value = `Deck "${deck.name}" guardado.`;
  } catch (e) {
    // Se muestra el mensaje de la API: dice qué cartas fallan o qué nombre choca.
    error.value = e.message;
  } finally {
    enviando.value = false;
  }
}

onMounted(cargar);
watch(() => props.versionToken, cargar);
</script>

<template>
  <section v-if="!haySesion" class="tarjeta">
    <h2>Armar deck</h2>
    <p class="nota">Inicia sesión con Google como jugador para armar un deck.</p>
  </section>

  <section v-else class="tarjeta">
    <h2>Armar deck</h2>

    <form @submit.prevent="guardar">
      <CampoFormulario etiqueta="Nombre del deck">
        <input v-model="nombre" maxlength="50" placeholder="Mi deck de fuego" />
      </CampoFormulario>

      <p class="nota">
        Cartas elegidas: <strong>{{ cantidad }} / {{ TAMANO_DECK }}</strong>
        <span v-if="!completo"> · Elige exactamente {{ TAMANO_DECK }}.</span>
      </p>

      <button type="submit" class="primario" :disabled="!puedeGuardar">
        {{ enviando ? "Guardando..." : "Guardar deck" }}
      </button>

      <p v-if="error" class="mensaje-error">{{ error }}</p>
      <p v-if="aviso" class="nota">{{ aviso }}</p>
    </form>

    <p v-if="cartas.length === 0" class="nota">
      Todavía no tienes cartas en tu colección.
    </p>

    <!-- Una carta por cada carta distinta de la colección. El botón va en la ranura de
         CartaVista, así el componente de la carta no necesita saber nada de decks. -->
    <ul class="cartas">
      <li v-for="carta in cartas" :key="carta.cardId">
        <CartaVista
          :nombre="carta.name"
          :tipo="carta.type"
          :rareza="carta.rarity"
          :imagen="carta.thumbnailUrl"
          :copias="carta.copies"
          :seleccionada="estaElegida(carta.cardId)"
        >
          <button
            type="button"
            :disabled="!estaElegida(carta.cardId) && completo"
            @click="alternar(carta.cardId)"
          >
            {{ estaElegida(carta.cardId) ? "Quitar del deck" : "Agregar al deck" }}
          </button>
        </CartaVista>
      </li>
    </ul>

    <h3>Tus decks</h3>
    <p v-if="decks.length === 0" class="nota">Todavía no tienes decks.</p>
    <ul v-else class="decks">
      <li v-for="deck in decks" :key="deck.id">
        <strong>{{ deck.name }}</strong>
        <span class="nota"> · {{ deck.cardCount }} cartas</span>
        <button type="button" class="ver-deck" @click="verDeck(deck.id)">
          {{ deckAbierto?.id === deck.id ? "Ocultar cartas" : "Ver cartas" }}
        </button>
      </li>
    </ul>

    <p v-if="cargandoDeck" class="nota">Cargando deck...</p>
    <p v-if="errorDeck" class="mensaje-error">{{ errorDeck }}</p>

    <!-- Detalle del deck elegido: las 12 cartas completas. El costo y la habilidad van en la
         ranura de CartaVista (el componente solo sabe de poder), igual que el botón de arriba. -->
    <div v-if="deckAbierto" class="detalle-deck">
      <h4>{{ deckAbierto.name }}</h4>
      <ul class="cartas">
        <li v-for="carta in deckAbierto.cards" :key="carta.cardId">
          <CartaVista
            :nombre="carta.name"
            :tipo="carta.type"
            :rareza="carta.rarity"
            :imagen="carta.thumbnailUrl"
            :poder="carta.power"
          >
            <span class="detalle-extra">Costo: {{ carta.cost }}</span>
            <span class="detalle-extra">
              Habilidad: {{ textoHabilidad(carta) || "Sin habilidad" }}
            </span>
          </CartaVista>
        </li>
      </ul>
    </div>
  </section>
</template>

<style scoped>
/* Misma cuadrícula que la colección: tantas columnas como quepan. */
.cartas {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(150px, 1fr));
  gap: 0.75rem;
  list-style: none;
  padding: 0;
  margin: 1rem 0;
}

.decks {
  list-style: none;
  padding: 0;
  margin: 0;
}

.decks li {
  padding: 0.35rem 0;
  border-bottom: 1px solid var(--borde);
}

.ver-deck {
  margin-left: 0.75rem;
}

.detalle-extra {
  display: block;
  font-size: 0.85rem;
}
</style>
