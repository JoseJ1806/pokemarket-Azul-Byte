# pokemarket-Azul-Byte

## Mapa del proyecto

- `src/PokeArena.Api/Program.cs`: punto de entrada de la API, configuración de servicios, seguridad y endpoint `GET /api/cards`.
- `src/PokeArena.Api/Models/`: clases que representan los datos del dominio, como `Card`.
- `src/PokeArena.Api/Data/`: `DbContext` y factoría usada por Entity Framework Core para conectarse a PostgreSQL.
- `src/PokeArena.Api/Migrations/`: cambios versionados del esquema de la base de datos. Los archivos `*.Designer.cs` son generados por EF Core y no se editan manualmente.
- `src/PokeArena.GameEngine/`: espacio reservado para reglas del juego que todavía no forman parte de esta funcionalidad.
- `tests/PokeArena.Tests/`: pruebas de integración que levantan la API y comprueban seguridad, filtros, contrato JSON y paginación.
- `compose.yaml`: inicia PostgreSQL local para desarrollo y pruebas.
- `.gitignore`: evita incluir compilados, cachés y configuraciones locales en Git.

Cada archivo de código incluye comentarios de orientación sobre su responsabilidad
y sus conexiones. Los archivos JSON de configuración no se comentan porque JSON no
admite comentarios válidos; su propósito está explicado aquí y en `compose.yaml`.

## Migraciones

Las migraciones deben conservarse porque representan la evolución de la base de
datos y se aplican en este orden:

1. `20261019180701_InitialCreate`: crea la tabla inicial `cards`.
2. `20261019183024_ExpandCardForSearch`: agrega código, costo, poder, tipo, rareza e información de habilidades.
3. `20261019190000_EnableUnaccent`: habilita la búsqueda de texto sin distinguir tildes en PostgreSQL.

También debe conservarse `PokeArenaDbContextModelSnapshot.cs`, que Entity Framework
usa para comparar el modelo actual con futuras migraciones. Los archivos
`*.Designer.cs` contienen metadatos necesarios para reconstruir cada migración.
No se deben borrar ni editar manualmente. Solo sería válido reemplazar todas las
migraciones si se va a crear una base nueva y no existe ningún entorno o dato que
deba conservarse.

## Iniciar el proyecto

Requisitos: .NET 10, Docker Desktop y PostgreSQL mediante Docker Compose.

Desde la carpeta raíz del repositorio:

```powershell
docker compose up -d
```

Configure una clave local para validar los JWT. No la incluya en el código fuente:

```powershell
$env:Jwt__SigningKey = "clave-local-de-desarrollo-con-32-caracteres"
```

Aplique las migraciones:

```powershell
dotnet ef database update `
	--project src\PokeArena.Api\PokeArena.Api.csproj `
	--startup-project src\PokeArena.Api\PokeArena.Api.csproj
```

Inicie la API:

```powershell
dotnet run `
	--project src\PokeArena.Api\PokeArena.Api.csproj `
	--urls "http://localhost:5080"
```

La búsqueda queda disponible en `http://localhost:5080/api/cards` y requiere un
JWT válido con el rol `Admin`. El proyecto no genera tokens; deben provenir del
sistema de identidad configurado para el entorno.

Para comprobar la solución y ejecutar las pruebas:

```powershell
dotnet build PokeArena.slnx
dotnet test PokeArena.slnx
```

Las pruebas de integración utilizan una base separada llamada `pokearena_tests`.
No ejecutan limpieza ni recreación sobre la base de desarrollo `pokearena`.

## Buscar cartas

`GET /api/cards` requiere un JWT cuyo rol sea `Admin`. Configure `Jwt:SigningKey`
mediante secretos de desarrollo o variables de entorno; la aplicación no incluye
credenciales ni tokens predeterminados. Los parámetros `q`, `code`, `cost`,
`minCost`, `maxCost`, `power`, `minPower`, `maxPower`, `type`, `rarity`,
`abilityCode`, `abilityTrigger` y `abilityEffect` se combinan con lógica AND.

La respuesta tiene un máximo de 100 cartas. La primera petición devuelve
`nextCursor` cuando hay más resultados; envíe ese valor como `cursor` en la
siguiente petición. El cursor fija el mayor `Id` visible al iniciar el recorrido,
por lo que las cartas creadas después quedan fuera de ese recorrido y aparecen en
una búsqueda nueva. No combine `cursor` con `page`.

La búsqueda textual ignora mayúsculas y tildes, escapa `%`, `_` y `\\`, y usa la
extensión PostgreSQL `unaccent`. Aplique las migraciones antes de usar el endpoint.
El repositorio todavía no contiene un catálogo oficial centralizado de tipos ni
rareza; por eso esos filtros se comparan contra los valores persistidos y queda
pendiente definir y validar ese catálogo cuando producto lo publique.

Ejemplos:

```http
GET /api/cards?q=dragon&pageSize=20
Authorization: Bearer <jwt-admin>

GET /api/cards?type=Agua&rarity=Rara&minCost=2&maxCost=5&abilityCode=AB-01
Authorization: Bearer <jwt-admin>

GET /api/cards?pageSize=20&cursor=<nextCursor>
Authorization: Bearer <jwt-admin>
```