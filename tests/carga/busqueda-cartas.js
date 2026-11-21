// PRUEBA DE CARGA DE LA BÚSQUEDA DE CARTAS (tarea 222).
//
// Mide GET /api/cards con un catálogo de 10.000 cartas, usando k6. Simula a
// administradores que buscan por nombre, combinan filtros, buscan por código y recorren
// los resultados con "Cargar más" (cursor), y genera un extracto de latencias.
//
// Cómo se corre (con la API levantada, 10.000 cartas cargadas y un token de administrador):
//   k6 run tests/carga/busqueda-cartas.js -e BASE_URL=http://localhost:5080 -e TOKEN=<token>
// Ver tests/carga/README.md para los pasos completos.
//
// Etapas:
//   normal: 50 búsquedas por segundo durante 1 minuto. Aquí se exige la meta de latencia
//           del proyecto (p95 menor a 200 ms, OE-3 y D-05).
//   alta:   sube de 50 a 200 búsquedas por segundo en 1 minuto y se mantiene 30 segundos.
//           No tiene meta: sirve para ver cómo se degrada la búsqueda al aumentar la carga.
//
// Resultado: un resumen en consola y los archivos resultados-carga.md (extracto de
// latencias por etapa y por tipo de búsqueda) y resultados-carga.json (datos completos).

import http from 'k6/http';
import { check } from 'k6';

const BASE_URL = (__ENV.BASE_URL || 'http://localhost:5080').replace(/\/$/, '');
const TOKEN = __ENV.TOKEN;
const MIN_CARTAS = Number(__ENV.MIN_CARTAS || 10000);
const TASA_NORMAL = Number(__ENV.TASA_NORMAL || 50);
const TASA_ALTA = Number(__ENV.TASA_ALTA || 200);

if (!TOKEN) {
  throw new Error('Falta el token de administrador: k6 run ... -e TOKEN=<token>');
}

const params = (tipo) => ({
  headers: { Authorization: `Bearer ${TOKEN}` },
  tags: { tipo },
});

// Valores con los que buscan los administradores simulados. Algunos términos van sin
// tilde (raton, igneo) o en mayúsculas (ÁGUILA) para comprobar que la búsqueda los
// encuentra igual (unaccent e ILIKE).
const TERMINOS = ['drag', 'tortu', 'raton', 'búho', 'zorro', 'igneo', 'eléctr', 'somb', 'veloz', 'colib', 'ÁGUILA', 'lunar'];
const TIPOS = ['Fuego', 'Agua', 'Planta', 'Eléctrico', 'Psíquico', 'Lucha'];
const PREFIJOS = ['FUE', 'AGU', 'PLA', 'ELE', 'PSI', 'LUC'];
const RAREZAS = ['Común', 'Poco común', 'Rara', 'Legendaria'];

const TIPOS_BUSQUEDA = ['nombre', 'filtros', 'codigo', 'sin_criterios', 'siguiente_pagina'];

export const options = {
  scenarios: {
    normal: {
      executor: 'constant-arrival-rate',
      rate: TASA_NORMAL,
      timeUnit: '1s',
      duration: '1m',
      preAllocatedVUs: 20,
      maxVUs: 100,
      tags: { etapa: 'normal' },
    },
    alta: {
      executor: 'ramping-arrival-rate',
      startTime: '1m10s',
      startRate: TASA_NORMAL,
      timeUnit: '1s',
      preAllocatedVUs: 50,
      maxVUs: 300,
      stages: [
        { target: TASA_ALTA, duration: '1m' },
        { target: TASA_ALTA, duration: '30s' },
      ],
      tags: { etapa: 'alta' },
    },
  },
  thresholds: {
    // Metas: menos de 1 % de errores en toda la prueba y p95 menor a 200 ms en la etapa normal.
    http_req_failed: ['rate<0.01'],
    'http_req_duration{etapa:normal}': ['p(95)<200'],
    // Los siguientes umbrales siempre se cumplen. Están para que k6 calcule las
    // estadísticas de cada etapa y de cada tipo de búsqueda en el extracto.
    'http_req_duration{etapa:alta}': ['max>=0'],
    'http_reqs{etapa:normal}': ['count>=0'],
    'http_reqs{etapa:alta}': ['count>=0'],
    ...Object.fromEntries(TIPOS_BUSQUEDA.flatMap((tipo) => [
      [`http_req_duration{tipo:${tipo}}`, ['max>=0']],
      [`http_reqs{tipo:${tipo}}`, ['count>=0']],
    ])),
  },
  summaryTrendStats: ['avg', 'min', 'med', 'p(90)', 'p(95)', 'p(99)', 'max'],
};

const elegir = (lista) => lista[Math.floor(Math.random() * lista.length)];
const entre = (min, max) => min + Math.floor(Math.random() * (max - min + 1));

// Antes de medir: confirma que el token sirve y que el catálogo tiene las cartas esperadas.
export function setup() {
  const respuesta = http.get(`${BASE_URL}/api/cards?pageSize=1`, params('verificacion'));
  if (respuesta.status !== 200) {
    throw new Error(`La verificación inicial respondió ${respuesta.status}. Revise BASE_URL y TOKEN.`);
  }

  const total = respuesta.json('total');
  if (total < MIN_CARTAS) {
    throw new Error(`El catálogo tiene ${total} cartas y la prueba necesita al menos ${MIN_CARTAS}. Cargue los datos con tools/PokeArena.CargaDatos.`);
  }

  return { totalCartas: total, fecha: new Date().toISOString() };
}

function buscar(tipo, consulta) {
  const respuesta = http.get(`${BASE_URL}/api/cards?${consulta}`, params(tipo));
  check(respuesta, {
    'responde 200': (r) => r.status === 200,
    'trae la lista de cartas': (r) => r.status === 200 && Array.isArray(r.json('items')),
  });
  return respuesta;
}

// Cada iteración es una búsqueda de un administrador. Proporciones: 40 % por nombre,
// 30 % con filtros combinados, 15 % por código y 15 % sin criterios seguida de "Cargar más".
export default function () {
  const sorteo = Math.random();

  if (sorteo < 0.4) {
    buscar('nombre', `q=${encodeURIComponent(elegir(TERMINOS))}&pageSize=20`);
    return;
  }

  if (sorteo < 0.7) {
    const minCost = entre(0, 6);
    const consulta = [
      `type=${encodeURIComponent(elegir(TIPOS))}`,
      `rarity=${encodeURIComponent(elegir(RAREZAS))}`,
      `minCost=${minCost}`,
      `maxCost=${entre(minCost, 10)}`,
      'pageSize=20',
    ].join('&');
    buscar('filtros', consulta);
    return;
  }

  if (sorteo < 0.85) {
    buscar('codigo', `code=${encodeURIComponent(`PKA-${elegir(PREFIJOS)}-${entre(1, 99)}`)}&pageSize=20`);
    return;
  }

  const primera = buscar('sin_criterios', 'pageSize=50');
  const cursor = primera.status === 200 ? primera.json('nextCursor') : null;
  if (cursor) {
    buscar('siguiente_pagina', `pageSize=50&cursor=${encodeURIComponent(cursor)}`);
  }
}

// EXTRACTO DE LATENCIAS: arma un resumen en Markdown y guarda también los datos completos.
export function handleSummary(data) {
  const ms = (valor) => (typeof valor === 'number' ? `${valor.toFixed(1)} ms` : '—');
  const metrica = (clave) => data.metrics[clave];
  const cantidad = (clave) => metrica(clave)?.values?.count ?? 0;

  const fila = (nombre, claveDuracion, claveSolicitudes) => {
    const duracion = metrica(claveDuracion);
    if (!duracion) return null;
    const v = duracion.values;
    return `| ${nombre} | ${cantidad(claveSolicitudes)} | ${ms(v.avg)} | ${ms(v.med)} | ${ms(v['p(90)'])} | ${ms(v['p(95)'])} | ${ms(v['p(99)'])} | ${ms(v.max)} |`;
  };

  const encabezado = '| Grupo | Solicitudes | Promedio | Mediana | p90 | p95 | p99 | Máximo |\n| --- | --- | --- | --- | --- | --- | --- | --- |';

  const porEtapa = [
    fila('Toda la prueba', 'http_req_duration', 'http_reqs'),
    fila(`Etapa normal (${TASA_NORMAL} búsquedas/s)`, 'http_req_duration{etapa:normal}', 'http_reqs{etapa:normal}'),
    fila(`Etapa alta (${TASA_NORMAL} a ${TASA_ALTA} búsquedas/s)`, 'http_req_duration{etapa:alta}', 'http_reqs{etapa:alta}'),
  ].filter(Boolean);

  const nombresTipo = {
    nombre: 'Por nombre (parcial, sin tildes)',
    filtros: 'Tipo, rareza y rango de costo',
    codigo: 'Por código (parcial)',
    sin_criterios: 'Sin criterios, primera página de 50',
    siguiente_pagina: 'Cargar más (cursor), página de 50',
  };
  const porTipo = TIPOS_BUSQUEDA
    .map((tipo) => fila(nombresTipo[tipo], `http_req_duration{tipo:${tipo}}`, `http_reqs{tipo:${tipo}}`))
    .filter(Boolean);

  const umbrales = Object.entries(data.metrics)
    .flatMap(([clave, m]) => Object.entries(m.thresholds || {})
      .filter(([condicion]) => !condicion.startsWith('max>=') && !condicion.startsWith('count>='))
      .map(([condicion, estado]) => `| ${clave} | ${condicion} | ${estado.ok ? 'Cumple' : 'No cumple'} |`));

  const errores = metrica('http_req_failed')?.values?.rate ?? 0;
  const descartadas = cantidad('dropped_iterations');
  const tasa = metrica('http_reqs')?.values?.rate ?? 0;
  const setup = data.setup_data || {};

  const markdown = [
    '# Prueba de carga: búsqueda de cartas',
    '',
    `- Fecha: ${setup.fecha || '—'}`,
    `- Cartas en el catálogo: ${setup.totalCartas ?? '—'}`,
    `- Solicitudes por segundo en promedio: ${tasa.toFixed(1)}`,
    `- Errores HTTP: ${(errores * 100).toFixed(2)} %`,
    `- Búsquedas que k6 no pudo lanzar a tiempo (dropped_iterations): ${descartadas}`,
    `- Usuarios virtuales máximos: ${metrica('vus_max')?.values?.max ?? '—'}`,
    '',
    '## Latencia por etapa',
    '',
    encabezado,
    ...porEtapa,
    '',
    '## Latencia por tipo de búsqueda',
    '',
    encabezado,
    ...porTipo,
    '',
    '## Metas',
    '',
    '| Métrica | Meta | Resultado |',
    '| --- | --- | --- |',
    ...umbrales,
    '',
  ].join('\n');

  return {
    stdout: `\n${markdown}\n`,
    'resultados-carga.md': markdown,
    'resultados-carga.json': JSON.stringify(data, null, 2),
  };
}
