namespace PokeArena.Api.Shared;

// TIPOS DE ERROR DE NEGOCIO.
// Los casos de uso devuelven un DomainError en lugar de armar respuestas HTTP.
// ProblemResults lo traduce a un formato único y documentado (RNF-API-03).
public enum DomainErrorKind
{
    Validation,
    NotFound,
    Conflict
}

// Code es el tipo de error propio del dominio (por ejemplo invalid-field) y Field
// indica el campo o criterio que provocó el rechazo.
public sealed record DomainError(DomainErrorKind Kind, string Code, string Message, string? Field = null)
{
    public static DomainError Validation(string code, string message, string? field = null) =>
        new(DomainErrorKind.Validation, code, message, field);
}
