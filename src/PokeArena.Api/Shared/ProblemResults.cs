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
            // Agregados en el Sprint 2 para Inicio de sesión.
            // 401: la persona no pudo demostrar quién es (token del proveedor inválido).
            DomainErrorKind.Unauthorized => StatusCodes.Status401Unauthorized,
            // 503: un servicio externo, como Google, no respondió. No es culpa del cliente.
            DomainErrorKind.Unavailable => StatusCodes.Status503ServiceUnavailable,
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
