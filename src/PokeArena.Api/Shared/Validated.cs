namespace PokeArena.Api.Shared;

// RESULTADO DE UNA VALIDACIÓN: el valor ya validado y normalizado, o el primer error encontrado.
// Lo usan los validadores que separan las reglas de entrada del flujo de los casos de uso.
public sealed record Validated<T>(T? Value, DomainError? Error) where T : class
{
    public static Validated<T> Valid(T value) => new(value, null);

    public static Validated<T> Invalid(DomainError error) => new(null, error);

    public static Validated<T> Invalid(string code, string message, string field) =>
        new(null, DomainError.Validation(code, message, field));
}
