using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using PokeArena.Api.Modules.Identity.Application;
using PokeArena.Api.Modules.Identity.Domain;

namespace PokeArena.Api.Modules.Identity.Infrastructure;

// ADAPTADOR DE SALIDA: GOOGLE COMO PROVEEDOR DE IDENTIDAD (módulo Identidad y Acceso).
//
// Qué hace: implementa el puerto IExternalIdentityProvider para Google. Recibe el
// ID token que el navegador obtuvo de Google y decide si es auténtico.
// Es el patrón ADAPTADOR: traduce el mundo de Google (JWT firmado con RSA, claims
// "sub" y "email") al objeto ExternalIdentity que entiende nuestro dominio.
//
// Librería usada: Microsoft.IdentityModel (la misma familia que ya valida nuestros JWT
// desde el Sprint 1). No se agregó ningún paquete nuevo: viene incluida con
// Microsoft.AspNetCore.Authentication.JwtBearer. Además sigue el estándar OpenID Connect,
// así que un segundo proveedor que también lo siga se validaría casi igual.
//
// Las CUATRO comprobaciones que exige la US ("un token que no fue emitido por el
// proveedor, que está expirado, que tiene la firma inválida o que fue emitido para otra
// aplicación es rechazado"):
// 1. Firma:      el token debe estar firmado con una de las claves públicas de Google.
// 2. Emisor:     el claim "iss" debe ser accounts.google.com.
// 3. Audiencia:  el claim "aud" debe ser NUESTRO Client ID.
// 4. Expiración: el claim "exp" no puede haber pasado.
//
// Cómo obtiene las claves públicas de Google (bloque "opt" del diagrama de secuencia):
// Google publica un documento de configuración OpenID Connect con la dirección de sus
// claves (JWKS). El ConfigurationManager lo descarga la primera vez, lo guarda en caché
// y lo vuelve a descargar solo de vez en cuando. Es el patrón CACHE ASIDE. Por eso esta
// clase se registra como Singleton en IdentityModule: si se creara una por solicitud,
// la caché se perdería y se descargarían las claves en cada ingreso.
public sealed class GoogleIdentityProvider(
    IOptions<GoogleAuthOptions> options,
    IConfigurationManager<OpenIdConnectConfiguration> googleConfiguration) : IExternalIdentityProvider
{
    // Nombre del proveedor que se guarda en PlayerAccount.Provider.
    public const string ProviderName = "Google";

    // Documento de configuración público de Google. IdentityModule lo usa para crear
    // el ConfigurationManager real.
    public const string DiscoveryDocumentUrl = "https://accounts.google.com/.well-known/openid-configuration";

    // Google firma sus tokens con cualquiera de estos dos valores en "iss".
    // Su documentación pide aceptar los dos.
    private static readonly string[] GoogleIssuers = ["https://accounts.google.com", "accounts.google.com"];

    // El validador de JWT de Microsoft. Es seguro compartir una sola instancia.
    private static readonly JsonWebTokenHandler TokenHandler = new();

    public async Task<ExternalIdentityResult> ValidateAsync(string idToken, CancellationToken cancellationToken)
    {
        var clientId = options.Value.ClientId;

        // Sin Client ID no se puede comprobar la audiencia. Se responde 503 (y no 500)
        // para que quede claro que es un problema de configuración del servidor y no
        // del jugador. El resto de la API sigue funcionando.
        if (string.IsNullOrWhiteSpace(clientId))
            return ExternalIdentityResult.Unavailable(
                "El inicio de sesión con Google no está configurado en el servidor (falta Authentication:Google:ClientId).");

        // PRIMER INTENTO de validación con las claves que estén en caché.
        var outcome = await TryValidateAsync(idToken, clientId, cancellationToken);

        // Si Google cambió sus claves (lo hace periódicamente), el token puede venir
        // firmado con una clave nueva que todavía no está en nuestra caché. En ese caso
        // se pide refrescar la caché y se intenta UNA vez más.
        if (outcome.SignatureKeyNotFound)
        {
            googleConfiguration.RequestRefresh();
            outcome = await TryValidateAsync(idToken, clientId, cancellationToken);
        }

        return outcome.Result;
    }

    // Hace una validación completa y devuelve el resultado, más un aviso de si el
    // problema fue que no se encontró la clave de la firma.
    private async Task<(ExternalIdentityResult Result, bool SignatureKeyNotFound)> TryValidateAsync(
        string idToken,
        string clientId,
        CancellationToken cancellationToken)
    {
        // PASO A. Obtener las claves públicas de Google (de la caché o de internet).
        OpenIdConnectConfiguration configuration;
        try
        {
            configuration = await googleConfiguration.GetConfigurationAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // No se pudo hablar con Google (sin red, Google caído, respuesta rara).
            // Criterio de error de la US: "si el proveedor de identidad no está disponible,
            // el sistema responde con un error descriptivo y no deja ninguna cuenta en
            // estado parcial". Aquí todavía no se tocó la base de datos.
            return (ExternalIdentityResult.Unavailable(
                "No se pudo contactar a Google para validar el inicio de sesión. Intente de nuevo en unos minutos."), false);
        }

        // PASO B. Definir QUÉ se considera un token válido.
        var parameters = new TokenValidationParameters
        {
            // 1. Firma: solo con las claves públicas que Google publicó.
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = configuration.SigningKeys,
            RequireSignedTokens = true,

            // 2. Emisor: tiene que ser Google.
            ValidateIssuer = true,
            ValidIssuers = GoogleIssuers,

            // 3. Audiencia: tiene que ser nuestra aplicación y no otra.
            ValidateAudience = true,
            ValidAudience = clientId,

            // 4. Expiración: obligatoria y vigente. Se toleran 60 segundos de diferencia
            //    entre el reloj de Google y el nuestro, igual que en Program.cs del Sprint 1.
            ValidateLifetime = true,
            RequireExpirationTime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };

        // PASO C. Validar. Este método no lanza excepciones: devuelve IsValid y, si falla,
        // la excepción que explica por qué.
        var validation = await TokenHandler.ValidateTokenAsync(idToken, parameters);
        if (!validation.IsValid)
        {
            // Firma inválida, emisor ajeno, otra audiencia, expirado o ni siquiera un JWT.
            // No se le dice al cliente cuál de todos fue, para no darle pistas a alguien
            // que esté probando tokens falsos. El detalle técnico queda en la excepción.
            var keyNotFound = validation.Exception is SecurityTokenSignatureKeyNotFoundException;
            return (ExternalIdentityResult.InvalidToken(
                "El token de Google no es válido o ya expiró. Inicie sesión de nuevo."), keyNotFound);
        }

        // PASO D. Leer quién es la persona. "sub" es su identificador permanente en Google.
        var token = (JsonWebToken)validation.SecurityToken;
        var subject = token.Subject;
        if (string.IsNullOrWhiteSpace(subject))
            return (ExternalIdentityResult.InvalidToken("El token de Google no identifica a ninguna persona."), false);

        // El correo es solo informativo. Si por alguna razón no viene, se guarda vacío.
        // No se revisa "email_verified" porque la cuenta NO se identifica por correo.
        token.TryGetPayloadValue<string>("email", out var email);

        return (ExternalIdentityResult.Valid(new ExternalIdentity(ProviderName, subject, email ?? string.Empty)), false);
    }
}
