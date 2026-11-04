<script setup>
import { ref } from "vue";
import { buscarCartas } from "../api/cartas.js";

// Las mismas listas que usa el formulario de crear. Están duplicadas del backend,
// con el riesgo de desactualizarse que ya comentamos.
const TIPOS = ["Fuego", "Agua", "Planta", "Eléctrico", "Psíquico", "Lucha"];
const RAREZAS = ["Común", "Poco común", "Rara", "Legendaria"];

// Los filtros que el usuario llena. Las llaves coinciden con los nombres de los
// parámetros de la API, como espera buscarCartas. El texto vacío significa
// "sin filtro".
const filtros = ref({
  q: "",
  code: "",
  type: "",
  rarity: "",
  minCost: "",
  maxCost: "",
  minPower: "",
  maxPower: "",
  pageSize: 20,
});

// Todas las cartas encontradas hasta ahora. Crece cada vez que se pide otra página.
const cartas = ref([]);

// El total que reporta la API para esta búsqueda.
const total = ref(null);

// El cursor para pedir la página siguiente. null significa que no hay más páginas.
const siguienteCursor = ref(null);

const cargando = ref(false);
const error = ref("");

// Distingue "todavía no busqué" de "busqué y no hubo resultados".
// Son dos situaciones distintas y el usuario debe verlas distintas.
const yaBusco = ref(false);

// Se ejecuta al presionar "Buscar": empieza una búsqueda NUEVA desde cero.
async function buscar() {
  cartas.value = [];
  total.value = null;
  siguienteCursor.value = null;
  error.value = "";
  await traerPagina(null);
}

// Se ejecuta al presionar "Cargar más": pide la página siguiente con el cursor
// guardado, y agrega sus cartas a las que ya están en pantalla.
async function cargarMas() {
  await traerPagina(siguienteCursor.value);
}

// Pide una página a la API y actualiza la pantalla con la respuesta.
async function traerPagina(cursor) {
  cargando.value = true;
  try {
    const respuesta = await buscarCartas(filtros.value, cursor);

    // push(...lista) agrega todos los elementos de la lista al final del arreglo,
    // en vez de reemplazarlo. Así "Cargar más" suma cartas y no borra las anteriores.
    cartas.value.push(...respuesta.items);
    total.value = respuesta.total;
    siguienteCursor.value = respuesta.nextCursor;
    yaBusco.value = true;
  } catch (e) {
    // La API devuelve { error: "..." } en las búsquedas inválidas, y el cliente
    // de la API ya lo convierte en e.message.
    error.value = `Error ${e.estado ?? ""}: ${e.message}`;
  } finally {
    // finally corre siempre, haya o no error, para que el botón no quede bloqueado.
    cargando.value = false;
  }
}
</script>

<template>
  <h2>Buscar cartas</h2>

  <!-- Cada campo se conecta a su filtro con v-model. v-model.number convierte el
       texto escrito a número, para que costo y poder viajen como números. -->
  <div>
    <label>Nombre contiene <input v-model="filtros.q" /></label>
  </div>
  <div>
    <label>Código contiene <input v-model="filtros.code" /></label>
  </div>
  <div>
    <label
      >Tipo
      <select v-model="filtros.type">
        <!-- value="" es "sin filtro": no se manda este parámetro a la API. -->
        <option value="">Todos</option>
        <option v-for="t in TIPOS" :key="t">{{ t }}</option>
      </select>
    </label>
  </div>
  <div>
    <label
      >Rareza
      <select v-model="filtros.rarity">
        <option value="">Todas</option>
        <option v-for="r in RAREZAS" :key="r">{{ r }}</option>
      </select>
    </label>
  </div>
  <div>
    <label
      >Costo desde <input v-model.number="filtros.minCost" type="number"
    /></label>
    <label
      >hasta <input v-model.number="filtros.maxCost" type="number"
    /></label>
  </div>
  <div>
    <label
      >Poder desde <input v-model.number="filtros.minPower" type="number"
    /></label>
    <label
      >hasta <input v-model.number="filtros.maxPower" type="number"
    /></label>
  </div>
  <div>
    <!-- Se deja editable para poder probar la paginación con pocas cartas. -->
    <label
      >Cartas por página <input v-model.number="filtros.pageSize" type="number"
    /></label>
  </div>

  <!-- :disabled bloquea el botón mientras hay una petición en curso. -->
  <button :disabled="cargando" @click="buscar">Buscar</button>

  <!-- Cada mensaje solo aparece en su situación, con v-if. -->
  <p v-if="error" style="color: crimson">{{ error }}</p>
  <p v-if="cargando">Buscando...</p>
  <p v-if="yaBusco && !error && cartas.length === 0">
    No hay cartas que coincidan con estos filtros.
  </p>

  <!-- Se muestra cuántas cartas hay en pantalla y el total que dice la API, uno al
       lado del otro, para poder compararlos al probar la paginación. -->
  <p v-if="total !== null">
    En pantalla: {{ cartas.length }}. Total según la API: {{ total }}.
  </p>

  <table v-if="cartas.length > 0">
    <thead>
      <tr>
        <th>Imagen</th>
        <th>Código</th>
        <th>Nombre</th>
        <th>Tipo</th>
        <th>Rareza</th>
        <th>Costo</th>
        <th>Poder</th>
        <th>Habilidad</th>
      </tr>
    </thead>
    <tbody>
      <!-- v-for dibuja una fila por carta. :key le da a Vue un identificador único
           para saber cuál fila es cuál cuando la lista cambia. -->
      <tr v-for="carta in cartas" :key="carta.id">
        <td>
          <!-- Si la miniatura falló, thumbnailUrl no existe: se muestra un texto. -->
          <img
            v-if="carta.thumbnailUrl"
            :src="carta.thumbnailUrl"
            width="50"
            alt=""
          />
          <span v-else>sin imagen</span>
        </td>
        <td>{{ carta.code }}</td>
        <td>{{ carta.name }}</td>
        <td>{{ carta.type }}</td>
        <td>{{ carta.rarity }}</td>
        <td>{{ carta.cost }}</td>
        <td>{{ carta.power }}</td>
        <td>{{ carta.abilityCode }}</td>
      </tr>
    </tbody>
  </table>

  <!-- El botón solo existe si la API dio un cursor, o sea, si hay más páginas.
       Este es el bucle "¿hay más resultados?" que el profe pidió modelar: se
       repite mientras nextCursor no sea null. -->
  <button v-if="siguienteCursor" :disabled="cargando" @click="cargarMas">
    Cargar más
  </button>
</template>
