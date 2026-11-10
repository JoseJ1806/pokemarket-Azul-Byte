using PokeArena.Api.Modules.Identity.Domain;
using PokeArena.Api.Shared;

namespace PokeArena.Api.Modules.Identity.Application;

// PUERTO: PROVEEDOR DE IDENTIDAD EXTERNO (módulo Identidad y Acceso, capa de aplicación).
//
// Qué es: el "enchufe" por el que el caso de uso le pregunta a un proveedor externo
// (Google en el MVP) si un token es auténtico y a quién pertenece.
//
// Por qué es una interfaz y no una clase concreta (arquitectura hexagonal, ADR-001):
// 1. El caso de uso SignInWithProviderUseCase no sabe nada de Google, de firmas RSA ni
//    de HTTP. Solo conoce este contrato.
// 2. En producción lo implementa GoogleIdentityProvider (Infrastructure). En las pruebas
//    se reemplaza por un doble falso que responde lo que la prueba necesite.
// 3. Agregar un segundo proveedor (Microsoft, Apple) es escribir otra clase que cumpla
//    este contrato, sin tocar el caso de uso. Esto es el patrón ESTRATEGIA y responde al
//    criterio alterno de la US: "agregar un segundo proveedor no requiere cambiar cómo
//    se autorizan las solicitudes a la API".
public interface IExternalIdentityProvider
{
    // Valida el token que el cliente obtuvo del proveedor. En el diagrama de secuencia
    // aparece como "ValidateAsync(idToken)". Nunca lanza excepciones por un token malo:
    // devuelve un ExternalIdentityResult que dice qué pasó.
    Task<ExternalIdentityResult> ValidateAsync(string idToken, CancellationToken cancellationToken);
}

// RESULTADO DE VALIDAR UN TOKEN EXTERNO.
// Sigue el mismo estilo que CreateCardResult del módulo Catálogo: o trae la identidad,
// o trae un DomainError, nunca las dos cosas.
//
// Los tres resultados posibles:
// 1. Valid:       el token es auténtico. Identity trae los datos de la persona.
// 2. InvalidToken: el token es falso, expiró, tiene otra audiencia u otro emisor.
//                 Termina en 401 (criterio de error de la US).
// 3. Unavailable: no se pudo consultar al proveedor (por ejemplo Google caído).
//                 Termina en 503 (criterio de error de la US).
public sealed record ExternalIdentityResult(ExternalIdentity? Identity, DomainError? Error)
{
    // Códigos de error que viajan en el campo "code" del ProblemDetails. El frontend y
    // las pruebas los usan para distinguir un caso del otro sin leer el mensaje.
    public const string InvalidTokenCode = "invalid-provider-token";
    public const string UnavailableCode = "identity-provider-unavailable";

    public static ExternalIdentityResult Valid(ExternalIdentity identity) => new(identity, null);

    public static ExternalIdentityResult InvalidToken(string message) =>
        new(null, DomainError.Unauthorized(InvalidTokenCode, message));

    public static ExternalIdentityResult Unavailable(string message) =>
        new(null, DomainError.Unavailable(UnavailableCode, message));
}
