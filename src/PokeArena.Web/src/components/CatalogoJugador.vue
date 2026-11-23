<script setup>
import { computed, onMounted, ref, watch } from "vue";
import { explorarCatalogo } from "../api/catalogo.js";

const props = defineProps({ versionToken: { type: Number, default: 0 } });

const TIPOS = ["Fuego", "Agua", "Planta", "Eléctrico", "Psíquico", "Lucha"];
const RAREZAS = ["Común", "Poco común", "Rara", "Legendaria"];
const filtros = ref({
  search: "",
  type: "",
  rarity: "",
  minPower: "",
  maxPower: "",
  abilityCode: "",
  onlyOwned: false,
  page: 1,
  pageSize: 20,
});
const respuesta = ref({ items: [], hasNextPage: false, hasPreviousPage: false });
const seleccionada = ref(null);
const cargando = ref(false);
const error = ref("");
const busquedaEnVivo = ref(null);

const cartas = computed(() => respuesta.value.items ?? []);

async function cargar() {
  cargando.value = true;
  error.value = "";
  try {
    respuesta.value = await explorarCatalogo(filtros.value);
    seleccionada.value = null;
  } catch (e) {
    error.value = e.message;
  } finally {
    cargando.value = false;
  }
}

function reiniciarPagina() {
  filtros.value.page = 1;
  cargar();
}

function programarBusqueda() {
  clearTimeout(busquedaEnVivo.value);
  busquedaEnVivo.value = setTimeout(reiniciarPagina, 300);
}

function limpiarFiltros() {
  Object.assign(filtros.value, {
    search: "", type: "", rarity: "", minPower: "", maxPower: "",
    abilityCode: "", onlyOwned: false, page: 1,
  });
  cargar();
}

function cambiarPagina(delta) {
  filtros.value.page = Math.max(1, filtros.value.page + delta);
  cargar();
}

onMounted(cargar);
watch(() => props.versionToken, cargar);
</script>

<template>
  <section class="catalogo-jugador">
    <div class="tarjeta">
      <h2>Explorar catálogo</h2>
      <div class="filtros-catalogo">
        <label>Buscar
          <input v-model="filtros.search" placeholder="Pikachu" @input="programarBusqueda" />
        </label>
        <label>Tipo
          <select v-model="filtros.type" @change="reiniciarPagina">
            <option value="">Todos</option>
            <option v-for="tipo in TIPOS" :key="tipo">{{ tipo }}</option>
          </select>
        </label>
        <label>Rareza
          <select v-model="filtros.rarity" @change="reiniciarPagina">
            <option value="">Todas</option>
            <option v-for="rareza in RAREZAS" :key="rareza">{{ rareza }}</option>
          </select>
        </label>
        <label>Poder mínimo
          <input v-model.number="filtros.minPower" type="number" min="-20" max="20" @change="reiniciarPagina" />
        </label>
        <label>Poder máximo
          <input v-model.number="filtros.maxPower" type="number" min="-20" max="20" @change="reiniciarPagina" />
        </label>
        <label>Habilidad
          <select v-model="filtros.abilityCode" @change="reiniciarPagina">
            <option value="">Todas</option>
            <option value="HAB-000">Sin habilidad</option>
          </select>
        </label>
        <label class="interruptor">
          <input v-model="filtros.onlyOwned" type="checkbox" @change="reiniciarPagina" />
          Solo mis cartas
        </label>
      </div>
      <button @click="limpiarFiltros">Limpiar filtros</button>
    </div>

    <p v-if="cargando" class="nota">Cargando catálogo...</p>
    <p v-if="error" class="mensaje-error">{{ error }}</p>
    <p v-if="!cargando && !error && cartas.length === 0" class="nota">
      No hay cartas que coincidan con los filtros.
    </p>

    <div class="catalogo-contenido">
      <ul v-if="cartas.length" class="rejilla-cartas">
        <li
          v-for="carta in cartas"
          :key="carta.id"
          class="carta-catalogo"
          :class="{ seleccionada: seleccionada?.id === carta.id }"
          @mouseenter="seleccionada = carta"
          @focusin="seleccionada = carta"
          @click="seleccionada = carta"
        >
          <img v-if="carta.imageUrl" :src="carta.imageUrl" :alt="carta.name" />
          <div v-else class="sin-imagen">Sin imagen</div>
          <strong>{{ carta.name }}</strong>
          <span>{{ carta.type }} · {{ carta.rarity }}</span>
          <span>Poder: {{ carta.power }}</span>
          <span class="copias" :class="{ posee: carta.isOwned }">
            {{ carta.isOwned ? `Posees x${carta.quantityOwned}` : "No poseída" }}
          </span>
        </li>
      </ul>

      <aside v-if="seleccionada" class="detalle-carta tarjeta">
        <h3>{{ seleccionada.name }}</h3>
        <img v-if="seleccionada.imageUrl" :src="seleccionada.imageUrl" :alt="`Imagen grande de ${seleccionada.name}`" />
        <p>{{ seleccionada.type }} · {{ seleccionada.rarity }}</p>
        <p>Copias: {{ seleccionada.quantityOwned }}</p>
      </aside>
    </div>

    <nav v-if="cartas.length || respuesta.hasPreviousPage" class="paginacion" aria-label="Paginación">
      <button :disabled="!respuesta.hasPreviousPage || cargando" @click="cambiarPagina(-1)">Anterior</button>
      <span>Página {{ filtros.page }}</span>
      <button :disabled="!respuesta.hasNextPage || cargando" @click="cambiarPagina(1)">Siguiente</button>
    </nav>
  </section>
</template>

<style scoped>
.filtros-catalogo { display: grid; grid-template-columns: repeat(auto-fit, minmax(150px, 1fr)); gap: .75rem; }
.filtros-catalogo label { display: flex; flex-direction: column; gap: .25rem; }
.interruptor { flex-direction: row !important; align-items: center; padding-top: 1.5rem; }
.catalogo-contenido { display: grid; grid-template-columns: minmax(0, 1fr) 260px; gap: 1rem; }
.rejilla-cartas { display: grid; grid-template-columns: repeat(auto-fill, minmax(150px, 1fr)); gap: .75rem; padding: 0; list-style: none; }
.carta-catalogo { display: flex; flex-direction: column; gap: .3rem; padding: .6rem; border: 1px solid var(--borde); border-radius: 8px; cursor: pointer; }
.carta-catalogo img, .detalle-carta img { width: 100%; border-radius: 6px; object-fit: cover; }
.carta-catalogo.seleccionada { outline: 3px solid var(--acento); }
.copias { font-weight: 600; color: var(--texto-suave); }
.copias.posee { color: var(--acento); }
.detalle-carta { align-self: start; position: sticky; top: 1rem; }
.paginacion { display: flex; justify-content: center; gap: 1rem; align-items: center; margin: 1rem 0; }
@media (max-width: 700px) { .catalogo-contenido { grid-template-columns: 1fr; } .detalle-carta { position: static; } }
</style>
