<script setup>
import { ref, onMounted, watch } from "vue";
import { buscarCartas, listarHabilidades } from "../api/cartas.js";
import CampoFormulario from "./CampoFormulario.vue";

// Dato que App.vue le pasa a esta pantalla. Cuando cambia, el token se renovó.
const props = defineProps({
  versionToken: { type: Number, default: 0 },
});

// Las mismas listas que usa el formulario de crear. Están duplicadas del backend,
// con el riesgo de desactualizarse que ya comentamos.
const TIPOS = ["Fuego", "Agua", "Planta", "Eléctrico", "Psíquico", "Lucha"];
const RAREZAS = ["Común", "Poco común", "Rara", "Legendaria"];

// Los filtros que el usuario llena. Las llaves coinciden con los nombres de los
// parámetros de la API, como espera buscarCartas. El texto vacío significa
// "sin filtro". Todos los filtros llenos se combinan con AND: una carta debe
// cumplirlos todos a la vez.
const filtros = ref({
  q: "",
  code: "",
  type: "",
  rarity: "",
  minCost: "",
  maxCost: "",
  minPower: "",
  maxPower: "",
  abilityCode: "",
  abilityTrigger: "",
  abilityEffect: "",
  pageSize: 20,
});

// La lista de habilidades para el desplegable, y un aviso si no se pudo cargar.
const habilidades = ref([]);
const avisoHabilidades = ref("");

// Todas las cartas encontradas hasta ahora. Crece cada vez que se pide otra página.
const cartas = ref([]);
// El total que reporta la API para esta búsqueda.
const total = ref(null);
// El cursor para pedir la página siguiente. null significa que no hay más páginas.
const siguienteCursor = ref(null);

const cargando = ref(false);
const error = ref("");
// Distingue "todavía no busqué" de "busqué y no hubo resultados".
const yaBusco = ref(false);

// Pide la lista de habilidades a la API. Si falla (por ejemplo, el token no sirve),
// no bloquea la pantalla: muestra un aviso y se puede seguir buscando sin ese filtro.
async function cargarHabilidades() {
  try {
    habilidades.value = await listarHabilidades();
    avisoHabilidades.value = "";
  } catch (e) {
    avisoHabilidades.value = `No se pudo cargar la lista de habilidades: ${e.message}`;
  }
}

// Se pide al abrir la pantalla...
onMounted(cargarHabilidades);
// ...y otra vez cada vez que App.vue avisa que se guardó un token nuevo.
watch(() => props.versionToken, cargarHabilidades);

// Se ejecuta al presionar "Buscar": empieza una búsqueda NUEVA desde cero.
async function buscar() {
  cartas.value = [];
  total.value = null;
  siguienteCursor.value = null;
  error.value = "";

  // Si la lista de habilidades quedó vacía, se vuelve a intentar antes de buscar.
  if (habilidades.value.length === 0) await cargarHabilidades();

  await traerPagina(null);
}

// Se ejecuta al presionar "Cargar más": pide la página siguiente con el cursor
// guardado y AGREGA sus cartas a las que ya están en pantalla.
async function cargarMas() {
  await traerPagina(siguienteCursor.value);
}

async function traerPagina(cursor) {
  cargando.value = true;
  try {
    const respuesta = await buscarCartas(filtros.value, cursor);
    // push(...lista) agrega los elementos al final del arreglo, sin reemplazarlo.
    cartas.value.push(...respuesta.items);
    total.value = respuesta.total;
    siguienteCursor.value = respuesta.nextCursor;
    yaBusco.value = true;
  } catch (e) {
    // La API devuelve { error: "..." } en las búsquedas inválidas; el cliente de la
    // API lo convierte en e.message.
    error.value = `Error ${e.estado ?? ""}: ${e.message}`;
  } finally {
    // finally corre siempre, haya o no error, para que el botón no quede bloqueado.
    cargando.value = false;
  }
}
</script>

<template>
  <div>
    <h2>Buscar cartas</h2>

    <!-- Los filtros, dentro de una tarjeta para separarlos visualmente de los resultados. -->
    <div class="tarjeta">
      <div class="fila">
        <CampoFormulario etiqueta="Nombre contiene">
          <input v-model="filtros.q" />
        </CampoFormulario>
        <CampoFormulario etiqueta="Código contiene">
          <input v-model="filtros.code" />
        </CampoFormulario>
      </div>

      <div class="fila">
        <CampoFormulario etiqueta="Tipo">
          <select v-model="filtros.type">
            <!-- value="" es "sin filtro": no se manda este parámetro a la API. -->
            <option value="">Todos</option>
            <option v-for="t in TIPOS" :key="t">{{ t }}</option>
          </select>
        </CampoFormulario>
        <CampoFormulario etiqueta="Rareza">
          <select v-model="filtros.rarity">
            <option value="">Todas</option>
            <option v-for="r in RAREZAS" :key="r">{{ r }}</option>
          </select>
        </CampoFormulario>
      </div>

      <!-- v-model.number convierte el texto escrito a número, para que costo y poder
           viajen como números. -->
      <div class="fila">
        <CampoFormulario etiqueta="Costo desde">
          <input v-model.number="filtros.minCost" type="number" />
        </CampoFormulario>
        <CampoFormulario etiqueta="Costo hasta">
          <input v-model.number="filtros.maxCost" type="number" />
        </CampoFormulario>
        <CampoFormulario etiqueta="Poder desde">
          <input v-model.number="filtros.minPower" type="number" />
        </CampoFormulario>
        <CampoFormulario etiqueta="Poder hasta">
          <input v-model.number="filtros.maxPower" type="number" />
        </CampoFormulario>
      </div>

      <div class="fila">
        <CampoFormulario etiqueta="Habilidad">
          <!-- El desplegable usa las habilidades reales de la API y manda su CÓDIGO. -->
          <select v-model="filtros.abilityCode">
            <option value="">Todas</option>
            <option v-for="h in habilidades" :key="h.code" :value="h.code">
              {{ h.code }} - {{ h.name }}
            </option>
          </select>
        </CampoFormulario>
        <!-- Texto parcial: ignora mayúsculas y tildes, igual que el nombre. -->
        <CampoFormulario etiqueta="Disparador contiene">
          <input v-model="filtros.abilityTrigger" />
        </CampoFormulario>
        <CampoFormulario etiqueta="Efecto contiene">
          <input v-model="filtros.abilityEffect" />
        </CampoFormulario>
      </div>
      <p v-if="avisoHabilidades" class="mensaje-error">
        {{ avisoHabilidades }}
      </p>

      <div class="fila">
        <CampoFormulario etiqueta="Cartas por página">
          <input v-model.number="filtros.pageSize" type="number" />
        </CampoFormulario>
      </div>

      <!-- :disabled bloquea el botón mientras hay una petición en curso. -->
      <button class="primario" :disabled="cargando" @click="buscar">
        Buscar
      </button>
    </div>

    <!-- Cada mensaje solo aparece en su situación, con v-if. -->
    <p v-if="error" class="mensaje-error">{{ error }}</p>
    <p v-if="cargando" class="nota">Buscando...</p>
    <p v-if="yaBusco && !error && cartas.length === 0" class="nota">
      No hay cartas que coincidan con estos filtros.
    </p>

    <!-- Se muestran las dos cifras juntas para poder compararlas al probar la paginación. -->
    <p v-if="total !== null" class="nota">
      En pantalla: {{ cartas.length }}. Total según la API: {{ total }}.
    </p>

    <!-- tabla-scroll deja que la tabla se desplace a los lados en pantallas angostas. -->
    <div v-if="cartas.length > 0" class="tabla-scroll">
      <table>
        <thead>
          <tr>
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
            <!-- El estado de la imagen: si la miniatura falló, la carta queda "Fallida"
                 y no disponible, y el administrador necesita verlo. -->
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
            <!-- La habilidad con sus parámetros: cada línea aparece solo si ese dato
                 existe. Una carta sin habilidad solo muestra su código. -->
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
    </div>

    <!-- El botón solo existe si la API dio un cursor, o sea, si hay más páginas.
         Este es el bucle "¿hay más resultados?": se repite mientras nextCursor no sea null. -->
    <p v-if="siguienteCursor">
      <button :disabled="cargando" @click="cargarMas">Cargar más</button>
    </p>
  </div>
</template>
