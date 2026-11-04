<script setup>
import { ref, onMounted } from "vue";
import { listarHabilidades, crearCarta } from "../api/cartas.js";

const TIPOS = ["Fuego", "Agua", "Planta", "Eléctrico", "Psíquico", "Lucha"];
const RAREZAS = ["Común", "Poco común", "Rara", "Legendaria"];

const habilidades = ref([]);
const nombre = ref("");
const costo = ref(1);
const poder = ref(0);
const tipo = ref(TIPOS[0]);
const rareza = ref(RAREZAS[0]);
const codigoHabilidad = ref("HAB-000");
const imagen = ref(null);

const llave = ref(crypto.randomUUID());
const enviando = ref(false);
const mensaje = ref("");
const errorGeneral = ref("");
const errorCampo = ref("");
const errorCampoNombre = ref("");

onMounted(async () => {
  try {
    habilidades.value = await listarHabilidades();
  } catch (e) {
    errorGeneral.value = `No se pudieron cargar las habilidades: ${e.message}`;
  }
});

function elegirArchivo(evento) {
  imagen.value = evento.target.files[0] ?? null;
}

async function enviar() {
  mensaje.value = "";
  errorGeneral.value = "";
  errorCampo.value = "";
  errorCampoNombre.value = "";
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
    if (estado === 201) llave.value = crypto.randomUUID();
  } catch (e) {
    if (e.campo) {
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
  <h2>Crear carta</h2>
  <p v-if="errorGeneral" style="color: crimson">{{ errorGeneral }}</p>
  <p v-if="errorCampo" style="color: crimson">
    Problema en el campo «{{ errorCampoNombre }}»: {{ errorCampo }}
  </p>

  <div>
    <label>Nombre <input v-model="nombre" maxlength="200" /></label>
  </div>
  <div>
    <label>Costo (0 a 10) <input v-model.number="costo" type="number" /></label>
  </div>
  <div>
    <label
      >Poder (-20 a 20) <input v-model.number="poder" type="number"
    /></label>
  </div>
  <div>
    <label
      >Tipo
      <select v-model="tipo">
        <option v-for="t in TIPOS" :key="t">{{ t }}</option>
      </select>
    </label>
  </div>
  <div>
    <label
      >Rareza
      <select v-model="rareza">
        <option v-for="r in RAREZAS" :key="r">{{ r }}</option>
      </select>
    </label>
  </div>
  <div>
    <label
      >Habilidad
      <select v-model="codigoHabilidad">
        <option v-for="h in habilidades" :key="h.code" :value="h.code">
          {{ h.code }} - {{ h.name }}
        </option>
      </select>
    </label>
  </div>
  <div>
    <label
      >Imagen (PNG, JPG o WebP)
      <input
        type="file"
        accept="image/png,image/jpeg,image/webp"
        @change="elegirArchivo"
      />
    </label>
  </div>
  <button :disabled="enviando" @click="enviar">
    {{ enviando ? "Enviando..." : "Crear carta" }}
  </button>
  <pre>{{ mensaje }}</pre>
</template>
