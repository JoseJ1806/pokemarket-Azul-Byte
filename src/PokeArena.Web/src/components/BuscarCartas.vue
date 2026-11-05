<script setup>
// onMounted para pedir las habilidades al abrir la pantalla.
import { ref, onMounted } from "vue";
//  listarHabilidades, que ya existía para el formulario de crear carta.
import { buscarCartas, listarHabilidades } from "../api/cartas.js";

const TIPOS = ["Fuego", "Agua", "Planta", "Eléctrico", "Psíquico", "Lucha"];
const RAREZAS = ["Común", "Poco común", "Rara", "Legendaria"];

// Los filtros que el usuario llena. Las llaves coinciden con los nombres de los
// parámetros de la API. El texto vacío significa "sin filtro".
const filtros = ref({
  q: "",
  code: "",
  type: "",
  rarity: "",
  minCost: "",
  maxCost: "",
  minPower: "",
  maxPower: "",
  //  los tres filtros de habilidad. Como todos los demás, se combinan con AND:
  // una carta debe cumplir todos los que estén llenos.
  abilityCode: "",
  abilityTrigger: "",
  abilityEffect: "",
  pageSize: 20,
});

// la lista de habilidades para el desplegable, y un aviso si no se pudo cargar.
const habilidades = ref([]);
const avisoHabilidades = ref("");

const cartas = ref([]);
const total = ref(null);
const siguienteCursor = ref(null);
const cargando = ref(false);
const error = ref("");
const yaBusco = ref(false);

// pide la lista de habilidades a la API. Si falla (por ejemplo, todavía no
// hay token guardado), no bloquea la pantalla: muestra un aviso y se puede seguir
// buscando sin ese filtro.
async function cargarHabilidades() {
  try {
    habilidades.value = await listarHabilidades();
    avisoHabilidades.value = "";
  } catch (e) {
    avisoHabilidades.value = `No se pudo cargar la lista de habilidades: ${e.message}`;
  }
}

// onMounted se ejecuta una vez, cuando la pantalla aparece.
onMounted(cargarHabilidades);

async function buscar() {
  cartas.value = [];
  total.value = null;
  siguienteCursor.value = null;
  error.value = "";

  // NUEVO: si la lista quedó vacía (por ejemplo, porque el token se guardó después
  // de abrir la página), se vuelve a intentar antes de buscar.
  if (habilidades.value.length === 0) await cargarHabilidades();

  await traerPagina(null);
}

async function cargarMas() {
  await traerPagina(siguienteCursor.value);
}

async function traerPagina(cursor) {
  cargando.value = true;
  try {
    const respuesta = await buscarCartas(filtros.value, cursor);
    cartas.value.push(...respuesta.items);
    total.value = respuesta.total;
    siguienteCursor.value = respuesta.nextCursor;
    yaBusco.value = true;
  } catch (e) {
    error.value = `Error ${e.estado ?? ""}: ${e.message}`;
  } finally {
    cargando.value = false;
  }
}
</script>

<template>
  <h2>Buscar cartas</h2>

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

  <!-- NUEVO: filtros de habilidad. -->
  <div>
    <label
      >Habilidad
      <!-- El desplegable usa las habilidades reales de la API y manda su CÓDIGO.
           Elegir "HAB-000 - Sin Habilidad" busca las cartas sin habilidad. -->
      <select v-model="filtros.abilityCode">
        <option value="">Todas</option>
        <option v-for="h in habilidades" :key="h.code" :value="h.code">
          {{ h.code }} - {{ h.name }}
        </option>
      </select>
    </label>
  </div>
  <p v-if="avisoHabilidades" style="color: crimson">{{ avisoHabilidades }}</p>
  <div>
    <!-- Texto parcial: ignora mayúsculas y tildes, igual que el nombre. -->
    <label
      >Disparador contiene <input v-model="filtros.abilityTrigger"
    /></label>
    <label>Efecto contiene <input v-model="filtros.abilityEffect" /></label>
  </div>

  <div>
    <label
      >Cartas por página <input v-model.number="filtros.pageSize" type="number"
    /></label>
  </div>

  <button :disabled="cargando" @click="buscar">Buscar</button>

  <p v-if="error" style="color: crimson">{{ error }}</p>
  <p v-if="cargando">Buscando...</p>
  <p v-if="yaBusco && !error && cartas.length === 0">
    No hay cartas que coincidan con estos filtros.
  </p>
  <p v-if="total !== null">
    En pantalla: {{ cartas.length }}. Total según la API: {{ total }}.
  </p>

  <table v-if="cartas.length > 0">
    <thead>
      <tr>
        <!--  columna "Estado imagen". -->
        <th>Imagen</th>
        <th>Estado imagen</th>
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
      <tr v-for="carta in cartas" :key="carta.id">
        <td>
          <img
            v-if="carta.thumbnailUrl"
            :src="carta.thumbnailUrl"
            width="50"
            alt=""
          />
          <span v-else>sin imagen</span>
        </td>
        <!-- el estado de la imagen. Si la miniatura falló, la carta queda con
             estado "Fallida" y no disponible, y el administrador necesita verlo. -->
        <td>
          {{ carta.imageStatus }}
          <div v-if="!carta.isAvailable">No disponible</div>
        </td>
        <td>{{ carta.code }}</td>
        <td>{{ carta.name }}</td>
        <td>{{ carta.type }}</td>
        <td>{{ carta.rarity }}</td>
        <td>{{ carta.cost }}</td>
        <td>{{ carta.power }}</td>
        <!-- la habilidad con sus parámetros. Cada línea aparece solo si ese
             dato existe: una carta sin habilidad solo muestra su código. -->
        <td>
          {{ carta.abilityCode }}
          <div v-if="carta.abilityTrigger">
            Disparador: {{ carta.abilityTrigger }}
          </div>
          <div v-if="carta.abilityEffect">
            Efecto: {{ carta.abilityEffect }}
          </div>
          <div v-if="carta.abilityAffectedType">
            Tipo afectado: {{ carta.abilityAffectedType }}
          </div>
        </td>
      </tr>
    </tbody>
  </table>

  <button v-if="siguienteCursor" :disabled="cargando" @click="cargarMas">
    Cargar más
  </button>
</template>
