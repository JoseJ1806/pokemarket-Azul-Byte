<script setup>
// CampoFormulario: un campo de formulario con su etiqueta y, si hace falta, su error.
//
// Existe para NO repetir en cada pantalla la misma estructura (etiqueta + campo +
// mensaje). Las pantallas solo le dicen qué etiqueta mostrar y qué error tiene el
// campo; este componente se encarga de dibujarlo siempre igual.
//
// defineProps declara los datos que el componente RECIBE desde quien lo usa:
//   etiqueta: el texto que se muestra arriba del campo.
//   error:    el mensaje de error de ESTE campo. Vacío significa "sin error".
defineProps({
  etiqueta: { type: String, required: true },
  error: { type: String, default: "" },
});
</script>

<template>
  <!-- :class agrega la clase "con-error" solo cuando hay un mensaje; el CSS usa esa
       clase para pintar el borde del campo de rojo. -->
  <div class="campo" :class="{ 'con-error': error }">
    <label>
      <span>{{ etiqueta }}</span>
      <!-- <slot /> es el HUECO donde la pantalla coloca su campo (un input, un select...).
           Así este componente no necesita saber de qué tipo es el campo. -->
      <slot />
    </label>
    <small v-if="error" class="mensaje-error">{{ error }}</small>
  </div>
</template>
