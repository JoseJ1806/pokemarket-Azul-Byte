<script setup>
import { ref } from "vue";
import { crearUbicacion } from "../api/ubicaciones.js";
import CampoFormulario from "./CampoFormulario.vue";

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

// Los campos que esta pantalla puede marcar con un error propio. Los nombres son los
// que devuelve la API en el campo "field". Si llega un error de un campo que NO está
// en esta lista, se muestra como error general para que nunca se pierda.
// 'effectParameters' es el nombre que se espera para los errores de los parámetros
// del efecto; si la API usara otro, el error igual se vería como error general.
const CAMPOS_CON_ERROR = [
  "name",
  "description",
  "cardLimit",
  "energyMode",
  "energyAmount",
  "effectType",
  "effectParameters",
  "image",
];

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

// Estado de la solicitud.
const llave = ref(crypto.randomUUID());
const enviando = ref(false);
const mensaje = ref("");
const errorGeneral = ref("");
const errorCampo = ref("");
const errorCampoNombre = ref("");

function elegirArchivo(evento) {
  imagen.value = evento.target.files[0] ?? null;
}

// Devuelve el error de UN campo, o texto vacío si ese campo no tiene error.
function errorDe(campo) {
  return errorCampoNombre.value === campo ? errorCampo.value : "";
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
  <!-- Un solo elemento raíz que envuelve toda la pantalla. -->
  <div>
    <h2>Crear ubicación</h2>
    <p v-if="errorGeneral" class="mensaje-error">{{ errorGeneral }}</p>

    <CampoFormulario etiqueta="Nombre" :error="errorDe('name')">
      <input v-model="nombre" maxlength="200" />
    </CampoFormulario>

    <CampoFormulario etiqueta="Descripción" :error="errorDe('description')">
      <input v-model="descripcion" maxlength="1000" />
    </CampoFormulario>

    <CampoFormulario
      etiqueta="Límite de cartas por jugador (0 a 5)"
      :error="errorDe('cardLimit')"
    >
      <input v-model.number="limiteCartas" type="number" />
    </CampoFormulario>

    <div class="fila">
      <CampoFormulario
        etiqueta="Modalidad de energía extra"
        :error="errorDe('energyMode')"
      >
        <select v-model="modalidadEnergia">
          <option value="Fija">Fija (por turno)</option>
          <option value="PorCartas">Por cartas (por cada carta propia)</option>
        </select>
      </CampoFormulario>
      <CampoFormulario
        etiqueta="Cantidad de energía extra"
        :error="errorDe('energyAmount')"
      >
        <input v-model.number="cantidadEnergia" type="number" />
      </CampoFormulario>
    </div>

    <CampoFormulario etiqueta="Efecto" :error="errorDe('effectType')">
      <select v-model="tipoEfecto">
        <option v-for="e in EFECTOS" :key="e.valor" :value="e.valor">
          {{ e.etiqueta }}
        </option>
      </select>
    </CampoFormulario>

    <!-- Cada bloque aparece solo con su efecto: v-if lo crea o lo quita del todo. -->
    <div v-if="tipoEfecto === 'BonificacionPorTipo'" class="tarjeta">
      <div class="fila">
        <CampoFormulario etiqueta="Tipo de carta">
          <select v-model="tipoBonificacion">
            <option v-for="t in TIPOS" :key="t">{{ t }}</option>
          </select>
        </CampoFormulario>
        <CampoFormulario etiqueta="Cantidad de poder (-5 a 5)">
          <input v-model.number="cantidad" type="number" />
        </CampoFormulario>
      </div>
    </div>

    <div v-if="tipoEfecto === 'ModificadorDeCosto'" class="tarjeta">
      <div class="fila">
        <CampoFormulario etiqueta="Cambio al costo (-5 a 5)">
          <input v-model.number="cantidad" type="number" />
        </CampoFormulario>
        <CampoFormulario etiqueta="Tipo afectado">
          <select v-model="tipoAfectado">
            <!-- value="" significa "sin tipo": el efecto afecta a todas las cartas. -->
            <option value="">Todos (opcional)</option>
            <option v-for="t in TIPOS" :key="t">{{ t }}</option>
          </select>
        </CampoFormulario>
      </div>
    </div>

    <div v-if="tipoEfecto === 'RestriccionPorTurno'" class="tarjeta">
      <p class="nota">
        Turnos en los que NO se pueden jugar cartas en esta ubicación:
      </p>
      <div class="casillas">
        <!-- Una casilla por turno. v-model sobre un arreglo agrega o quita el valor de
             :value de esa lista cuando se marca o desmarca la casilla. -->
        <label v-for="turno in TURNOS" :key="turno" class="casilla">
          <input type="checkbox" v-model="turnosElegidos" :value="turno" />
          Turno {{ turno }}
        </label>
      </div>
    </div>

    <!-- Error de los parámetros del efecto: aplica al bloque de efecto que esté visible. -->
    <p v-if="errorDe('effectParameters')" class="mensaje-error">
      {{ errorDe("effectParameters") }}
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
      {{ enviando ? "Enviando..." : "Crear ubicación" }}
    </button>

    <!-- v-if: el resultado solo se dibuja cuando hay algo que mostrar. -->
    <pre v-if="mensaje">{{ mensaje }}</pre>
  </div>
</template>
