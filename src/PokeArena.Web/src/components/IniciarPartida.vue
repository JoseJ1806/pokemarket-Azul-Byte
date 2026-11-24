<script setup>
import { ref } from "vue";
import { iniciarPartida } from "../api/partidas.js";

const opponentPlayerId = ref("");
const challengerDeckCardIds = ref("");
const opponentDeckCardIds = ref("");
const locationId = ref("");
const cargando = ref(false);
const error = ref("");
const partida = ref(null);

function parseDeck(value) {
  return value
    .split(",")
    .map((item) => Number(item.trim()))
    .filter((item) => Number.isInteger(item) && item > 0);
}

async function enviar() {
  cargando.value = true;
  error.value = "";
  partida.value = null;
  try {
    partida.value = await iniciarPartida(
      {
        opponentPlayerId: opponentPlayerId.value,
        challengerDeckCardIds: parseDeck(challengerDeckCardIds.value),
        opponentDeckCardIds: parseDeck(opponentDeckCardIds.value),
        locationId: Number(locationId.value),
      },
      crypto.randomUUID(),
    );
  } catch (e) {
    error.value = e.message;
  } finally {
    cargando.value = false;
  }
}
</script>

<template>
  <section class="tarjeta">
    <h2>Iniciar partida</h2>

    <label>
      ID del jugador rival
      <input v-model="opponentPlayerId" placeholder="GUID del rival" />
    </label>

    <label>
      IDs de cartas de mi mazo
      <input v-model="challengerDeckCardIds" placeholder="1, 2, 3" />
    </label>

    <label>
      IDs de cartas del mazo rival
      <input v-model="opponentDeckCardIds" placeholder="4, 5, 6" />
    </label>

    <label>
      ID de ubicación
      <input v-model="locationId" type="number" min="1" />
    </label>

    <button class="primario" :disabled="cargando" @click="enviar">
      {{ cargando ? "Iniciando..." : "Iniciar partida" }}
    </button>

    <p v-if="error" class="mensaje-error">{{ error }}</p>
    <div v-if="partida" class="tarjeta" aria-live="polite">
      <h3>Partida creada</h3>
      <p><strong>ID:</strong> {{ partida.matchId }}</p>
      <p><strong>Estado:</strong> {{ partida.status }}</p>
      <p><strong>Turno inicial:</strong> {{ partida.currentTurnPlayerId }}</p>
    </div>
  </section>
</template>
