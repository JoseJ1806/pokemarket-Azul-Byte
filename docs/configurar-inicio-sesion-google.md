# Configurar el inicio de sesión con Google en tu equipo

Guía para que cualquier integrante del equipo pueda correr PokéArena en su propia computadora con el inicio de sesión con Google funcionando (US Inicio de sesión con proveedor, Sprint 2).

## Qué es y qué no es

PokéArena usa Google **solo para verificar quién es la persona** que inicia sesión. Google muestra la ventana para elegir la cuenta y le entrega a la API un token firmado que dice quién es. Después de eso, la API emite su propia credencial y no vuelve a hablar con Google hasta el siguiente inicio de sesión.

En Google **no se hostea nada** y no se guarda ningún dato de PokéArena. La API, la base de datos y las cuentas de jugador siguen corriendo en tu computadora, y en el futuro en Azure. Tampoco tiene costo.

Para esto existe un proyecto en Google Cloud Console llamado **PokeArena**. Ahí está registrada la aplicación con un **Client ID**, que es el identificador público que Google usa para reconocer a PokéArena. No es un secreto (Google lo muestra en el navegador), pero no se guarda en el repo para que cada quien lo configure en su equipo.

## Antes de empezar

1. **Que te agreguen como usuario de prueba.** La aplicación de Google está en modo de prueba, así que solo pueden entrar los correos registrados. Pídele a quien administra el proyecto en Google Cloud que agregue tu correo de Google. Si no estás en la lista, Google muestra "Acceso bloqueado".
2. **Pedir el Client ID** en el chat del equipo. Tiene esta forma: `123456789012-abc123def456.apps.googleusercontent.com`.
3. **Tener lo de siempre:** .NET 10, Docker Desktop, `dotnet-ef` y Node.js (versión LTS). La guía general está en el README.

## Pasos (PowerShell, desde la raíz del repo)

### 1. Traer los cambios y levantar la base de datos

```powershell
git pull
docker compose up -d
```

### 2. Aplicar las migraciones

Hay una migración nueva (`AddPlayerAccounts`) que crea la tabla de cuentas de jugador.

```powershell
cd src\PokeArena.Api
dotnet ef database update
```

### 3. Configurar los secretos de la API

Siguiendo en `src\PokeArena.Api`. La clave de firma probablemente ya la tienes del Sprint 1. El Client ID es nuevo.

```powershell
dotnet user-secrets set "Jwt:SigningKey" "clave-local-de-desarrollo-con-32-caracteres"
dotnet user-secrets set "Authentication:Google:ClientId" "<CLIENT-ID-DEL-EQUIPO>"
dotnet user-secrets list
```

El último comando muestra lo que quedó guardado. Revisa que el Client ID sea el real y no el texto de ejemplo.

Los secretos de usuario se guardan en tu perfil de Windows, no dentro del repo. Por eso no aparecen en Git.

### 4. Configurar el frontend

```powershell
cd ..\PokeArena.Web
Copy-Item .env.example .env.local
notepad .env.local
```

Deja la línea así, con el **mismo** Client ID del paso 3, sin comillas ni espacios:

```text
VITE_GOOGLE_CLIENT_ID=<CLIENT-ID-DEL-EQUIPO>
```

Git ignora `.env.local` a propósito (regla `*.local`), así que no se sube al repo.

### 5. Levantar la API y el frontend (dos terminales)

Terminal 1, desde `src\PokeArena.Api`:

```powershell
dotnet run --launch-profile http
```

Terminal 2, desde `src\PokeArena.Web` (`npm install` solo la primera vez):

```powershell
npm install
npm run dev
```

La API corre en `http://localhost:5034` y el frontend en `http://localhost:5173`. Tienen que estar las dos encendidas.

### 6. Probar

1. Abre `http://localhost:5173`.
2. Arriba aparece la tarjeta **"Sesión de jugador"** con el botón **"Iniciar sesión con Google"**.
3. Presiónalo y elige tu cuenta.
4. Debe aparecer "¡Bienvenido a PokéArena!" y "Sesión iniciada como tu correo (rol Player, Elo 0)".
5. Si recargas la página, la sesión se mantiene durante 15 minutos.

## Token de administrador

Las cuentas que entran con Google son siempre de jugador. Para usar las pestañas de Crear carta, Crear ubicación y Buscar cartas sigue haciendo falta un token de administrador pegado en el cuadro "Token de desarrollo".

**Importante:** desde el Sprint 2 la API exige que todo token traiga emisor (`iss`) y audiencia (`aud`). Los tokens de administrador generados antes de este cambio dan 401. Genera uno nuevo desde `src\PokeArena.Web`:

```powershell
$env:Jwt__SigningKey = "clave-local-de-desarrollo-con-32-caracteres"
node scripts/generar-token.mjs
```

Usa la misma clave que pusiste en los secretos de la API.

## Si algo falla

| Lo que ves | Qué significa | Qué hacer |
| --- | --- | --- |
| No carga `http://localhost:5173` | El frontend no está corriendo | `npm run dev` en `src\PokeArena.Web` |
| Aviso rojo "Falta configurar VITE_GOOGLE_CLIENT_ID" | No existe `.env.local` o está vacío | Paso 4 y reiniciar `npm run dev` |
| Google dice "The OAuth client was not found" (`invalid_client`) | El Client ID está mal escrito o quedó el texto de ejemplo | Revisar `.env.local` y reiniciar `npm run dev` |
| Google dice "Acceso bloqueado" sin `invalid_client` | Tu correo no está como usuario de prueba | Pedir que te agreguen |
| Google dice `origin_mismatch` | El origen `http://localhost:5173` no está autorizado en Google Cloud | Avisar a quien administra el proyecto |
| Error 503 "no está configurado en el servidor" | Falta el Client ID en la API | Paso 3 y reiniciar la API |
| Error "El token de Google no es válido" | El Client ID de la API y el del frontend no son iguales | Poner el mismo en los dos y reiniciar ambos |
| Error 401 en las pestañas de administrador | Token de administrador viejo (sin `iss` ni `aud`) | Generar uno nuevo (sección anterior) |
| Error "relation player_accounts does not exist" | Falta aplicar la migración nueva | Paso 2 |

Si cambias `.env.local`, hay que detener `npm run dev` con Ctrl+C y volver a correrlo, porque Vite solo lee ese archivo al arrancar. Lo mismo pasa con los secretos y la API.

## Para quien administra el proyecto en Google Cloud

1. **Usuarios de prueba:** Google Auth Platform → Público → Usuarios de prueba. Ahí se agregan los correos del equipo y del profesor.
2. **Orígenes autorizados:** Google Auth Platform → Clientes → el cliente de PokéArena → Orígenes autorizados de JavaScript. Hoy son `http://localhost:5173` y `http://localhost`. Cuando la app se publique en Azure, hay que agregar ahí su dirección pública.
3. Conviene agregar a los demás integrantes como miembros del proyecto de Google Cloud, para que no dependa de una sola persona.
