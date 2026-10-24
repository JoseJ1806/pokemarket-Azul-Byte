# pokemarket-Azul-Byte

## Mapa del proyecto

- `src/PokeArena.Api/Program.cs`: punto de entrada de la API, configuración de servicios, seguridad, registro de los módulos y endpoint `GET /api/cards`.
- `src/PokeArena.Api/Models/`: entidades `Card` y `Ability`, compartidas por la búsqueda y las migraciones.
- `src/PokeArena.Api/Modules/`: monolito modular con núcleo hexagonal (ADR-001). Cada módulo tiene sus casos de uso, sus puertos y sus adaptadores:
  - `Catalog/`: módulo Catálogo de Cartas. `Domain/` guarda las reglas y los estados, `Application/` los casos de uso `CreateCardUseCase` y `GetCardUseCase` y el puerto `ICardRepository` (RepositorioCartas), `Infrastructure/` el adaptador `EfCardRepository` y `Http/` los endpoints `POST /api/cards` y `GET /api/cards/{id}`.
  - `Images/`: módulo Imágenes. `ProcessImageUseCase` valida la imagen y genera la versión pequeña con 3 intentos (ADR-002). Sus puertos son `IImageCodec` y `IImageStore` (AlmacenImagenes), implementados con SkiaSharp y el sistema de archivos.
  - `Identity/`: módulo Identidad y Acceso. Define la política `AdminOnly`.
- `src/PokeArena.Api/Shared/`: manejo de errores con formato único (`DomainError` y `ProblemResults`).
- `src/PokeArena.Api/Data/`: `DbContext` y factoría usada por Entity Framework Core para conectarse a PostgreSQL.
- `src/PokeArena.Api/Migrations/`: cambios versionados del esquema de la base de datos. Los archivos `*.Designer.cs` son generados por EF Core y no se editan manualmente.
- `src/PokeArena.GameEngine/`: espacio reservado para reglas del juego que todavía no forman parte de esta funcionalidad.
- `tests/PokeArena.Tests/`: pruebas de integración que levantan la API (seguridad, filtros, contrato JSON, paginación, creación, validación de imágenes, ciclos de evolución y concurrencia) y pruebas unitarias del caso de uso Crear Carta con dobles de prueba.
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
4. `20261021200158_CreateCardMvp`: agrega la llave de idempotencia, la miniatura y la disponibilidad a `cards`, y crea la tabla `abilities` con `HAB-000` (`Sin Habilidad`).
5. `20261021201439_RemoveLegacyCardFields`: elimina los campos antiguos `SetCode` y `CardNumber`.
6. `20261021211949_AddAbilityAffectedType`: agrega el tipo afectado por la habilidad a `abilities` y a `cards`.

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
	--launch-profile http
```

Con el perfil `http`, la API escucha en `http://localhost:5034`. La búsqueda queda disponible en `http://localhost:5034/api/cards` y requiere un
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

## Cómo se crea una carta

Para crear una carta, un administrador envía sus datos y una imagen a
`POST /api/cards`. La API hace todo el proceso en una sola solicitud:

1. Comprueba que los datos estén completos y sean válidos.
2. Busca la habilidad seleccionada. Si la carta no tiene habilidad, se debe usar
   `HAB-000` (`Sin Habilidad`).
3. Genera automáticamente un código para la carta, por ejemplo `PKA-AGU-001`.
4. Valida la imagen y genera una miniatura de 200 x 280 píxeles, con hasta 3
   intentos dentro de la misma solicitud.
5. Guarda las imágenes y la carta. La carta queda disponible solo si la miniatura
   se generó (estado de imagen `Lista`). Si fallaron los 3 intentos, se guarda con
   estado `Fallida` y no disponible, y el administrador lo ve en la respuesta. Si
   falla el guardado en la base de datos, se borran los archivos.

La petición debe usar `multipart/form-data`, incluir un encabezado
`Idempotency-Key` único y contener estos campos:

| Campo | Qué representa |
| --- | --- |
| `Name` | Nombre de la carta |
| `Cost` | Costo, entre 0 y 10 |
| `Power` | Poder, entre -20 y 20 |
| `Type` | `Fuego`, `Agua`, `Planta`, `Eléctrico`, `Psíquico` o `Lucha` |
| `Rarity` | `Común`, `Poco común`, `Rara` o `Legendaria` |
| `AbilityCode` | Código de una habilidad existente |
| `Image` | Imagen PNG, JPG o WebP |

La imagen no puede superar 5 MB, debe tener formato vertical 5:7 y una
resolución mínima de 600 x 840 píxeles. Las habilidades disponibles se pueden
consultar con `GET /api/abilities`.

Ejemplo con PowerShell:

```powershell
$form = @{
    Name = "Charizard"
    Cost = 5
    Power = 18
    Type = "Fuego"
    Rarity = "Rara"
    AbilityCode = "HAB-000"
    Image = Get-Item ".\charizard.png"
}

Invoke-RestMethod `
    -Uri "http://localhost:5034/api/cards" `
    -Method Post `
    -Headers @{
        Authorization = "Bearer <jwt-admin>"
        "Idempotency-Key" = [guid]::NewGuid().ToString()
    } `
    -Form $form
```

Si se repite la misma solicitud con el mismo `Idempotency-Key`, la API devuelve
la carta existente en lugar de crear una segunda (200 en lugar de 201). Si la misma
llave se usa con datos distintos (nombre, costo, poder, tipo, rareza o habilidad),
la API responde 409 con `code` `idempotency-key-conflict`. La imagen no se compara
entre solicitudes. Si algún dato o la imagen no es válido, la API responde 400 con un
error en formato `application/problem+json`, con los campos `code` y `field`, y la
carta no se guarda.

Los códigos se generan por tipo con el formato `PKA-<TIPO>-<NNN>`. Para calcular el
siguiente, la API consulta solo el código más alto del tipo, y los bloqueos de
PostgreSQL (advisory locks) evitan duplicados cuando dos administradores crean
cartas del mismo tipo al mismo tiempo.

## Ficha de una carta

`GET /api/cards/{id}` requiere un JWT con rol `Admin` y devuelve la carta con el
detalle de su habilidad en el objeto `ability` (código, nombre, evento, efecto, tipo
afectado, si es de evolución y la carta destino). Si la carta no tiene habilidad,
`ability` es `null`. Muestra también las cartas con imagen `Pendiente` o `Fallida`, para
que el administrador pueda ver el estado de la imagen. Si el id no existe, responde
404 con `code` `card-not-found`.
