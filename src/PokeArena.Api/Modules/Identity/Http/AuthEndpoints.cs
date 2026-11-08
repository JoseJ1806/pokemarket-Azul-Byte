using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;
using PokeArena.Api.Modules.Identity.Application;
using PokeArena.Api.Shared;

namespace PokeArena.Api.Modules.Identity.Http;

// ADAPTADOR DE ENTRADA HTTP del módulo Identidad y Acceso.
//
// Igual que CardEndpoints en Catálogo: traduce la solicitud HTTP a una llamada al caso
// de uso y el resultado a una respuesta HTTP. NO contiene reglas de negocio. Toda la
// lógica vive en SignInWithProviderUseCase y GetCurrentPlayerUseCase.
//
// Endpoints que publica:
// POST /api/auth/google  Inicia sesión con un ID token de Google. Es anónimo, porque
//                        justamente es la forma de conseguir una credencial.
// GET  /api/me           Devuelve la cuenta dueña del token. Exige estar autenticado.
public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapSignInWithGoogle(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/auth/google", SignInWithGoogleAsync)
            .WithName("SignInWithGoogle")
            // AllowAnonymous: quien llama todavía no tiene credencial de PokéArena.
            .AllowAnonymous()
            // Estas líneas no cambian el comportamiento. Documentan las respuestas
            // posibles en el contrato OpenAPI (visible en /openapi/v1.json).
            .Produces<SignInResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return app;
    }

    public static IEndpointRouteBuilder MapGetCurrentPlayer(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/me", GetCurrentPlayerAsync)
            .WithName("GetCurrentPlayer")
            // RequireAuthorization() sin política: basta con un token válido de PokéArena,
            // sin importar el rol. Sin token, ASP.NET responde 401 antes de llegar aquí.
            .RequireAuthorization()
            .Produces<PlayerResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized);

        return app;
    }

    // POST /api/auth/google
    // Primer paso del diagrama del lado de la API: "ExecuteAsync(idToken)".
    private static async Task<IResult> SignInWithGoogleAsync(
        SignInWithGoogleRequest? request,
        SignInWithProviderUseCase useCase,
        CancellationToken cancellationToken)
    {
        var result = await useCase.ExecuteAsync(request?.IdToken, cancellationToken);

        // Error: ProblemResults lo traduce a 400, 401 o 503 con formato ProblemDetails,
        // el mismo formato de errores que el resto de la API.
        if (result.Error is { } error)
            return ProblemResults.From(error);

        // Éxito: 200 con la credencial propia y los datos del jugador.
        return Results.Ok(new SignInResponse(
            result.AccessToken!.Value,
            result.AccessToken.ExpiresAt,
            PlayerResponse.From(result.Account!),
            result.IsNewAccount,
            // ToString() convierte el enum en su nombre ("Granted"...).
            result.InitialCollection!.Value.ToString()));
    }

    // GET /api/me
    // ClaimsPrincipal user: ASP.NET lo llena con los datos del JWT ya validado.
    private static async Task<IResult> GetCurrentPlayerAsync(
        ClaimsPrincipal user,
        GetCurrentPlayerUseCase useCase,
        CancellationToken cancellationToken)
    {
        // Al validar el token, ASP.NET renombra el claim "sub" a ClaimTypes.NameIdentifier.
        // Se buscan los dos nombres por si esa conversión se desactiva algún día.
        var subject = user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? user.FindFirstValue(JwtRegisteredClaimNames.Sub);

        var result = await useCase.ExecuteAsync(subject, cancellationToken);
        return result.Error is { } error
            ? ProblemResults.From(error)
            : Results.Ok(PlayerResponse.From(result.Account!));
    }
}
