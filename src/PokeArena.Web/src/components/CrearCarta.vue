<script setup>
import { ref, onMounted, watch } from "vue";
import { listarHabilidades, crearCarta } from "../api/cartas.js";
import CampoFormulario from "./CampoFormulario.vue";

// Dato que App.vue le pasa a esta pantalla. Cuando cambia, el token se renovó.
const props = defineProps({
  versionToken: { type: Number, default: 0 },
});

const TIPOS = ["Fuego", "Agua", "Planta", "Eléctrico", "Psíquico", "Lucha"];
const RAREZAS = ["Común", "Poco común", "Rara", "Legendaria"];

// Los campos que esta pantalla puede marcar con un error propio. Los nombres son los
// que devuelve la API en el campo "field". Si llega un error de un campo que NO está
// en esta lista, se muestra como error general para que nunca se pierda.
const CAMPOS_CON_ERROR = [
  "name",
  "cost",
  "power",
  "type",
  "rarity",
  "abilityCode",
  "image",
];

// Lo que el usuario llena.
const nombre = ref("");
const costo = ref(1);
const poder = ref(0);
const tipo = ref(TIPOS[0]);
const rareza = ref(RAREZAS[0]);
const imagen = ref(null);

// Arranca vacío: no se asume ninguna habilidad hasta que la lista cargue.
const codigoHabilidad = ref("");
const habilidades = ref([]);
const cargandoHabilidades = ref(false);
const errorHabilidades = ref("");

// Estado del envío.
const llave = ref(crypto.randomUUID());
const enviando = ref(false);
const mensaje = ref("");
const errorGeneral = ref("");
const errorCampo = ref("");
const errorCampoNombre = ref("");

async function cargarHabilidades() {
  cargandoHabilidades.value = true;
  errorHabilidades.value = "";
  try {
    habilidades.value = await listarHabilidades();
    // Solo cuando la lista cargó se preselecciona "Sin habilidad", y queda visible.
    const existeSinHabilidad = habilidades.value.some(
      (h) => h.code === "HAB-000",
    );
    if (!codigoHabilidad.value && existeSinHabilidad)
      codigoHabilidad.value = "HAB-000";
  } catch (e) {
    errorHabilidades.value = `No se pudieron cargar las habilidades: ${e.message}`;
  } finally {
    cargandoHabilidades.value = false;
  }
}

// Se pide la lista al abrir la pantalla...
onMounted(cargarHabilidades);

// ...y se vuelve a pedir cada vez que App.vue avisa que se guardó un token nuevo.
// watch vigila un valor: cuando cambia, ejecuta la función que le damos.
// Se escribe () => props.versionToken, y no props.versionToken a secas, para que
// Vue pueda seguir el valor y enterarse de sus cambios.
watch(() => props.versionToken, cargarHabilidades);

function elegirArchivo(evento) {
  imagen.value = evento.target.files[0] ?? null;
}

// Devuelve el error de UN campo, o texto vacío si ese campo no tiene error.
// Cada CampoFormulario le pregunta por el suyo: errorDe('name'), errorDe('cost')...
function errorDe(campo) {
  return errorCampoNombre.value === campo ? errorCampo.value : "";
}

async function enviar() {
  mensaje.value = "";
  errorGeneral.value = "";
  errorCampo.value = "";
  errorCampoNombre.value = "";

  // No se envía una habilidad que el usuario no pudo ver ni elegir.
  if (!codigoHabilidad.value) {
    errorCampoNombre.value = "abilityCode";
    errorCampo.value =
      "Elige una habilidad. Si la lista está vacía, usa «Reintentar».";
    return;
  }

  enviando.value = true;
  try {
    const { estado, cuerpo } = await crearCarta(
      {
        nombre: nombre.value,
        costo: costo.value,
        poder: poder.value,
        tipo: tipo.value,
        rareza: rareza.value,
        codigoHabilidad: codigoHabilidad.value,
      },
      imagen.value,
      llave.value,
    );
    mensaje.value =
      (estado === 201
        ? "Carta creada."
        : "Esa solicitud ya había creado la carta (reintento).") +
      "\n" +
      JSON.stringify(cuerpo, null, 2);
    // Tras crear de verdad, la próxima solicitud necesita su propia llave.
    if (estado === 201) llave.value = crypto.randomUUID();
  } catch (e) {
    if (e.campo && CAMPOS_CON_ERROR.includes(e.campo)) {
      errorCampoNombre.value = e.campo;
      errorCampo.value = e.message;
    } else {
      errorGeneral.value = `Error ${e.estado ?? ""}: ${e.message}`;
    }
  } finally {
    enviando.value = false;
  }
}
</script>

<template>
  <div>
    <h2>Crear carta</h2>
    <p v-if="errorGeneral" class="mensaje-error">{{ errorGeneral }}</p>

    <CampoFormulario etiqueta="Nombre" :error="errorDe('name')">
      <input v-model="nombre" maxlength="200" />
    </CampoFormulario>

    <!-- class="fila" pone varios campos en la misma línea. -->
    <div class="fila">
      <CampoFormulario etiqueta="Costo (0 a 10)" :error="errorDe('cost')">
        <input v-model.number="costo" type="number" />
      </CampoFormulario>
      <CampoFormulario etiqueta="Poder (-20 a 20)" :error="errorDe('power')">
        <input v-model.number="poder" type="number" />
      </CampoFormulario>
    </div>

    <div class="fila">
      <CampoFormulario etiqueta="Tipo" :error="errorDe('type')">
        <select v-model="tipo">
          <option v-for="t in TIPOS" :key="t">{{ t }}</option>
        </select>
      </CampoFormulario>
      <CampoFormulario etiqueta="Rareza" :error="errorDe('rarity')">
        <select v-model="rareza">
          <option v-for="r in RAREZAS" :key="r">{{ r }}</option>
        </select>
      </CampoFormulario>
    </div>

    <CampoFormulario etiqueta="Habilidad" :error="errorDe('abilityCode')">
      <select v-model="codigoHabilidad" :disabled="cargandoHabilidades">
        <option value="" disabled>
          {{ cargandoHabilidades ? "Cargando..." : "Sin habilidades cargadas" }}
        </option>
        <option v-for="h in habilidades" :key="h.code" :value="h.code">
          {{ h.code }} - {{ h.name }}
        </option>
      </select>
    </CampoFormulario>
    <p v-if="errorHabilidades" class="mensaje-error">{{ errorHabilidades }}</p>
    <p v-if="habilidades.length === 0 && !cargandoHabilidades">
      <button @click="cargarHabilidades">Reintentar</button>
    </p>

    <CampoFormulario
      etiqueta="Imagen (PNG, JPG o WebP)"
      :error="errorDe('image')"
    >
      <input
        type="file"
        accept="image/png,image/jpeg,image/webp"
        @change="elegirArchivo"
      />
    </CampoFormulario>

    <button class="primario" :disabled="enviando" @click="enviar">
      {{ enviando ? "Enviando..." : "Crear carta" }}
    </button>

    <!-- v-if: el resultado solo se dibuja cuando hay algo que mostrar. -->
    <pre v-if="mensaje">{{ mensaje }}</pre>
  </div>
</template>
