# Prueba de carga de la búsqueda de cartas

Herramienta de medición de la tarea 222. Mide `GET /api/cards` con un catálogo de 10.000 cartas y genera un extracto de latencias.

| Pieza | Qué hace |
| --- | --- |
| `tools/PokeArena.CargaDatos` | Aplica las migraciones y carga 10.000 cartas de prueba deterministas (misma semilla, mismas cartas en cada corrida). Si ya existen, no las duplica |
| `tests/carga/busqueda-cartas.js` | Script de k6. Simula administradores que buscan por nombre, combinan filtros, buscan por código y recorren resultados con "Cargar más" |
| `.github/workflows/carga.yml` | Corre todo en GitHub Actions y publica el extracto en el resumen del workflow |

## Qué mide

La prueba tiene dos etapas:

- **Normal:** 50 búsquedas por segundo durante 1 minuto. Debe cumplir la meta de latencia del proyecto: p95 menor a 200 ms (OE-3 y D-05).
- **Alta:** sube de 50 a 200 búsquedas por segundo en 1 minuto y se mantiene 30 segundos. No tiene meta, sirve para ver cómo se degrada la búsqueda.

En toda la prueba los errores HTTP deben ser menos del 1 %.

Las búsquedas se reparten así: 40 % por nombre (coincidencia parcial, con y sin tildes), 30 % por tipo, rareza y rango de costo, 15 % por código y 15 % sin criterios seguida de "Cargar más" con el cursor.

## Correrla en GitHub Actions

El workflow **Prueba de carga** corre solo cuando un push o un Pull Request hacia `develop` cambia la prueba, la herramienta de datos o la búsqueda. El extracto aparece en el resumen de la ejecución y los archivos completos quedan en el artefacto `resultados-prueba-carga`.

## Correrla en la computadora

1. Levantar PostgreSQL:

   ```powershell
   docker compose up -d
   ```

2. Cargar las 10.000 cartas:

   ```powershell
   dotnet run --project tools/PokeArena.CargaDatos -- 10000
   ```

3. Levantar la API sin registrar cada consulta SQL, para que el log no afecte la medición. Usa la misma clave `Jwt__SigningKey` que tenés en los secretos de la API:

   ```powershell
   $env:Logging__LogLevel__Default = "Warning"
   dotnet run --project src/PokeArena.Api --configuration Release --urls http://localhost:5080
   ```

4. En otra terminal, generar un token de administrador con la misma clave:

   ```powershell
   $env:Jwt__SigningKey = "la-misma-clave-de-la-api"
   $env:TOKEN = node src/PokeArena.Web/scripts/generar-token.mjs
   ```

5. Instalar k6 (una sola vez) y correr la prueba desde esta carpeta:

   ```powershell
   winget install k6 --source winget
   cd tests/carga
   k6 run busqueda-cartas.js -e BASE_URL=http://localhost:5080
   ```

El resumen aparece en la consola y queda en `resultados-carga.md` y `resultados-carga.json`. Esos archivos no se suben al repositorio.

## Variables opcionales

| Variable | Valor por defecto | Para qué |
| --- | --- | --- |
| `BASE_URL` | `http://localhost:5080` | Dirección de la API |
| `TOKEN` | (obligatoria) | Token de administrador |
| `MIN_CARTAS` | `10000` | Cartas mínimas que exige antes de medir |
| `TASA_NORMAL` | `50` | Búsquedas por segundo de la etapa normal |
| `TASA_ALTA` | `200` | Búsquedas por segundo al final de la etapa alta |
