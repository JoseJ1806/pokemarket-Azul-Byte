<script setup>
import { ref } from "vue";
import { crearUbicacion } from "../api/ubicaciones.js";

// Los tipos de carta, para los efectos que piden elegir uno. Copiados del backend,
// con el mismo riesgo de desactualizarse que ya comentamos.
const TIPOS = ["Fuego", "Agua", "Planta", "Eléctrico", "Psíquico", "Lucha"];

// Los efectos posibles. 'valor' es el nombre EXACTO que espera la API y 'etiqueta' es
// lo que lee el usuario. Con value="" representamos "sin efecto".
const EFECTOS = [
  { valor: "", etiqueta: "Sin efecto" },
  { valor: "BonificacionPorTipo", etiqueta: "Bonificación por tipo" },
  { valor: "ModificadorDeCosto", etiqueta: "Modificador de costo" },
  { valor: "RestriccionPorTurno", etiqueta: "Restricción por turno" },
];

// Una partida tiene 6 turnos. Se usa para dibujar una casilla por turno.
const TURNOS = [1, 2, 3, 4, 5, 6];

// Datos generales de la ubicación.
const nombre = ref("");
const descripcion = ref("");
const limiteCartas = ref(3);
const modalidadEnergia = ref("Fija");
const cantidadEnergia = ref(0);
const imagen = ref(null);

// Datos del efecto. Solo se usan los que corresponden al efecto elegido.
const tipoEfecto = ref("");
const tipoBonificacion = ref(TIPOS[0]);
const cantidad = ref(1); // compartida por bonificación y modificador de costo
const tipoAfectado = ref(""); // opcional en el modificador de costo; '' = todos
const turnosElegidos = ref([]); // lista de números de turno marcados

// Estado de la solicitud. Mismo esquema que Crear carta.
const llave = ref(crypto.randomUUID());
const enviando = ref(false);
const mensaje = ref("");
const errorGeneral = ref("");
const errorCampo = ref("");
const errorCampoNombre = ref("");

function elegirArchivo(evento) {
  imagen.value = evento.target.files[0] ?? null;
}

// Convierte lo que el usuario llenó en el objeto de parámetros que cada efecto espera.
// Devuelve null cuando no hay efecto. Los nombres de las llaves (tipo, cantidad,
// tipoAfectado, turnos) deben coincidir con los que lee cada clase de efecto en la API.
function armarParametros() {
  if (tipoEfecto.value === "BonificacionPorTipo") {
    return { tipo: tipoBonificacion.value, cantidad: cantidad.value };
  }
  if (tipoEfecto.value === "ModificadorDeCosto") {
    const parametros = { cantidad: cantidad.value };
    // El tipo afectado es opcional: solo se incluye si el usuario eligió uno.
    if (tipoAfectado.value) parametros.tipoAfectado = tipoAfectado.value;
    return parametros;
  }
  if (tipoEfecto.value === "RestriccionPorTurno") {
    return { turnos: turnosElegidos.value };
  }
  return null;
}

async function enviar() {
  mensaje.value = "";
  errorGeneral.value = "";
  errorCampo.value = "";
  errorCampoNombre.value = "";
  enviando.value = true;
  try {
    const { estado, cuerpo } = await crearUbicacion(
      {
        nombre: nombre.value,
        descripcion: descripcion.value,
        limiteCartas: limiteCartas.value,
        modalidadEnergia: modalidadEnergia.value,
        cantidadEnergia: cantidadEnergia.value,
        tipoEfecto: tipoEfecto.value,
        parametrosEfecto: armarParametros(),
      },
      imagen.value,
      llave.value,
    );
    mensaje.value =
      (estado === 201
        ? "Ubicación creada."
        : "Esa solicitud ya había creado la ubicación (reintento).") +
      "\n" +
      JSON.stringify(cuerpo, null, 2);
    // Tras crear de verdad, la próxima solicitud necesita su propia llave.
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
  <h2>Crear ubicación</h2>
  <p v-if="errorGeneral" style="color: crimson">{{ errorGeneral }}</p>
  <p v-if="errorCampo" style="color: crimson">
    Problema en el campo «{{ errorCampoNombre }}»: {{ errorCampo }}
  </p>

  <div>
    <label>Nombre <input v-model="nombre" maxlength="200" /></label>
  </div>
  <div>
    <label>Descripción <input v-model="descripcion" maxlength="1000" /></label>
  </div>
  <div>
    <label
      >Límite de cartas por jugador (0 a 5)
      <input v-model.number="limiteCartas" type="number" />
    </label>
  </div>
  <div>
    <label
      >Modalidad de energía extra
      <select v-model="modalidadEnergia">
        <option value="Fija">Fija (por turno)</option>
        <option value="PorCartas">Por cartas (por cada carta propia)</option>
      </select>
    </label>
  </div>
  <div>
    <label
      >Cantidad de energía extra
      <input v-model.number="cantidadEnergia" type="number" />
    </label>
  </div>

  <div>
    <label
      >Efecto
      <select v-model="tipoEfecto">
        <option v-for="e in EFECTOS" :key="e.valor" :value="e.valor">
          {{ e.etiqueta }}
        </option>
      </select>
    </label>
  </div>

  <!-- Cada bloque aparece solo con su efecto: v-if lo crea o lo quita del todo. -->
  <div v-if="tipoEfecto === 'BonificacionPorTipo'">
    <label
      >Tipo de carta
      <select v-model="tipoBonificacion">
        <option v-for="t in TIPOS" :key="t">{{ t }}</option>
      </select>
    </label>
    <label
      >Cantidad de poder <input v-model.number="cantidad" type="number"
    /></label>
  </div>

  <div v-if="tipoEfecto === 'ModificadorDeCosto'">
    <label
      >Cantidad (cambio al costo)
      <input v-model.number="cantidad" type="number"
    /></label>
    <label
      >Tipo afectado
      <select v-model="tipoAfectado">
        <!-- value="" significa "sin tipo": el efecto afecta a todas las cartas. -->
        <option value="">Todos (opcional)</option>
        <option v-for="t in TIPOS" :key="t">{{ t }}</option>
      </select>
    </label>
  </div>

  <div v-if="tipoEfecto === 'RestriccionPorTurno'">
    Turnos en los que NO se pueden jugar cartas:
    <!-- Una casilla por turno. v-model sobre un arreglo agrega o quita el valor de
         :value de esa lista cuando se marca o desmarca la casilla. -->
    <label v-for="turno in TURNOS" :key="turno">
      <input type="checkbox" v-model="turnosElegidos" :value="turno" />
      {{ turno }}
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
    {{ enviando ? "Enviando..." : "Crear ubicación" }}
  </button>
  <pre>{{ mensaje }}</pre>
</template>
