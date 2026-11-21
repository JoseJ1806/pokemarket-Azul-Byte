<script setup>
// COMPONENTE CARTA (Sprint 3, refactor del frontend).
//
// Dibuja UNA carta: imagen, nombre, tipo y rareza. Antes, cada pantalla (la colección, el
// catálogo...) dibujaba la carta por su cuenta y con su propio estilo. Ahora todas usan
// este componente, así una carta se ve igual en todos lados y un cambio de diseño se hace
// en un solo archivo.
//
// Por qué recibe valores sueltos (nombre, imagen, tipo...) y no el objeto de la carta:
// cada endpoint de la API devuelve la carta con campos distintos (la colección usa
// "thumbnailUrl" y "cardId"; el catálogo usa "imageUrl" y "id"). Cada pantalla traduce sus
// datos una sola vez al usar el componente, y la carta no necesita saber de dónde vienen.
//
// Para qué sirven las ranuras: el contenido extra de cada pantalla (por ejemplo, el botón
// "Agregar al mazo" de Armar un Deck) se pasa dentro de la etiqueta del componente y
// aparece al pie de la carta, sin tener que modificar este archivo.
//
// Eventos: el componente NO declara eventos propios. Los eventos nativos que se pongan en
// la etiqueta (@click, @mouseenter, @focusin) pasan al elemento raíz automáticamente.

defineProps({
  // Datos básicos (obligatorios).
  nombre: { type: String, required: true },
  tipo: { type: String, required: true },
  rareza: { type: String, required: true },
  // Dirección de la imagen. Si no hay, se muestra el texto "Sin imagen".
  imagen: { type: String, default: null },
  // Poder de la carta. Si es null, no se muestra (la colección no lo necesita).
  poder: { type: Number, default: null },
  // Cantidad de copias que tiene el jugador. Se dibuja como una insignia "xN" en la
  // esquina, y solo cuando hay más de una.
  copias: { type: Number, default: null },
  // Línea de texto al pie (por ejemplo "Posees x2" o "No poseída"). Si es null, no se muestra.
  etiqueta: { type: String, default: null },
  // Resalta la etiqueta del pie con el color de acento (por ejemplo, para cartas poseídas).
  etiquetaDestacada: { type: Boolean, default: false },
  // Dibuja un contorno alrededor de la carta (por ejemplo, la carta elegida en una lista).
  seleccionada: { type: Boolean, default: false },
});
</script>

<template>
  <article class="carta" :class="{ seleccionada }">
    <img v-if="imagen" :src="imagen" :alt="nombre" />
    <div v-else class="sin-imagen">Sin imagen</div>

    <strong>{{ nombre }}</strong>
    <span class="detalle">{{ tipo }} · {{ rareza }}</span>
    <span v-if="poder !== null" class="detalle">Poder: {{ poder }}</span>
    <span
      v-if="etiqueta"
      class="etiqueta"
      :class="{ destacada: etiquetaDestacada }"
    >
      {{ etiqueta }}
    </span>

    <!-- Insignia con la cantidad de copias, solo cuando hay más de una. -->
    <span v-if="copias !== null && copias > 1" class="insignia"
      >x{{ copias }}</span
    >

    <!-- Contenido extra que ponga cada pantalla (botones, avisos...). -->
    <slot />
  </article>
</template>

<style scoped>
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
  object-fit: cover;
}

.sin-imagen {
  padding: 2rem 0;
  color: var(--texto-suave);
}

.detalle {
  color: var(--texto-suave);
  font-size: 0.9rem;
}

.etiqueta {
  font-weight: 600;
  color: var(--texto-suave);
}

.etiqueta.destacada {
  color: var(--acento);
}

.carta.seleccionada {
  outline: 3px solid var(--acento);
}

.insignia {
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
</style>
