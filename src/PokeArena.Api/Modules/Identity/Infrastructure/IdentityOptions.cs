namespace PokeArena.Api.Modules.Identity.Infrastructure;

// OPCIONES DE CONFIGURACIÓN del módulo Identidad y Acceso.
//
// Estas clases son el "molde" donde ASP.NET Core vacía los valores de appsettings.json,
// de los secretos de usuario o de las variables de entorno. IdentityModule las conecta
// con la sección correspondiente de la configuración.
//
// Ejemplo: en appsettings.json existe
//   "Jwt": { "Issuer": "pokearena", "Audience": "pokearena-api", "AccessTokenMinutes": 15 }
// y la clave "Jwt:SigningKey" vive en los secretos de usuario. Todo eso termina en un
// objeto JwtOptions.

// Sección "Jwt": cómo se firman y se validan las credenciales PROPIAS de PokéArena.
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    // Clave secreta para firmar (HMAC SHA256). Nunca va en el código ni en appsettings.json:
    // se configura con secretos de usuario o con la variable de entorno Jwt__SigningKey.
    // Debe tener al menos 32 caracteres, porque la librería rechaza claves más cortas.
    public string SigningKey { get; set; } = string.Empty;

    // Quién emite el token (claim "iss"). La API solo acepta tokens con este emisor.
    // Gracias a esto, un token de Google enviado directo a la API se rechaza (RNF-SEG-01).
    public string Issuer { get; set; } = string.Empty;

    // Para quién es el token (claim "aud"). La API solo acepta tokens con esta audiencia.
    public string Audience { get; set; } = string.Empty;

    // Minutos de vida de la credencial. La US pide un "tiempo de expiración corto".
    // El equipo decidió 15 minutos en todos los ambientes.
    public int AccessTokenMinutes { get; set; } = 15;
}

// Sección "Authentication:Google": datos para validar los ID tokens de Google.
public sealed class GoogleAuthOptions
{
    public const string SectionName = "Authentication:Google";

    // Client ID de la aplicación PokéArena registrada en Google Cloud Console.
    // NO es un secreto: Google lo muestra en el navegador al dibujar el botón.
    // Se usa como "audiencia" esperada: un token emitido para otra aplicación trae otro
    // Client ID y se rechaza (criterio de error de la US).
    // Si está vacío, el inicio de sesión con Google responde 503 explicando que falta
    // configurarlo, pero el resto de la API funciona normal.
    public string ClientId { get; set; } = string.Empty;
}
