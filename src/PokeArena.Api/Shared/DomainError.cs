namespace PokeArena.Api.Shared;

// TIPOS DE ERROR DE NEGOCIO.
// Los casos de uso devuelven un DomainError en lugar de armar respuestas HTTP.
// ProblemResults lo traduce a un formato único y documentado (RNF-API-03).
public enum DomainErrorKind
{
    Validation,
    NotFound,
    Conflict,

    // Agregado en el Sprint 2 para Inicio de sesión. Se usa cuando la persona no pudo
    // demostrar quién es, por ejemplo con un token de Google inválido o expirado.
    // ProblemResults lo traduce a 401 (RNF-SEG-03: "no sé quién eres").
    Unauthorized,

    // Agregado en el Sprint 2 para Inicio de sesión. Se usa cuando un servicio externo
    // del que dependemos no responde, por ejemplo Google. ProblemResults lo traduce a 503
    // para que el cliente sepa que el problema no es suyo y que puede reintentar.
    Unavailable
}

// Code es el tipo de error propio del dominio (por ejemplo invalid-field) y Field
// indica el campo o criterio que provocó el rechazo.
public sealed record DomainError(DomainErrorKind Kind, string Code, string Message, string? Field = null)
{
    public static DomainError Validation(string code, string message, string? field = null) =>
        new(DomainErrorKind.Validation, code, message, field);

    // Atajos agregados en el Sprint 2 con el mismo estilo que Validation.
    public static DomainError Unauthorized(string code, string message) =>
        new(DomainErrorKind.Unauthorized, code, message);

    public static DomainError Unavailable(string code, string message) =>
        new(DomainErrorKind.Unavailable, code, message);

    public static DomainError NotFound(string code, string message) =>
        new(DomainErrorKind.NotFound, code, message);
}
