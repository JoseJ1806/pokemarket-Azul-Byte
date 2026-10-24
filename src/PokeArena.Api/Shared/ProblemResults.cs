namespace PokeArena.Api.Shared;

// MANEJO DE ERRORES (adaptador de entrada).
// Traduce un DomainError a una respuesta ProblemDetails con un formato único:
// type propio del dominio, detail legible y las extensiones code y field.
public static class ProblemResults
{
    public static IResult From(DomainError error)
    {
        var status = error.Kind switch
        {
            DomainErrorKind.NotFound => StatusCodes.Status404NotFound,
            DomainErrorKind.Conflict => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest
        };

        var extensions = new Dictionary<string, object?> { ["code"] = error.Code };
        if (error.Field is not null)
            extensions["field"] = error.Field;

        return Results.Problem(
            detail: error.Message,
            statusCode: status,
            type: $"urn:pokearena:error:{error.Code}",
            extensions: extensions);
    }
}
