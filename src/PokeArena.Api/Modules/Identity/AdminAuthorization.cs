namespace PokeArena.Api.Modules.Identity;

// MÓDULO IDENTIDAD Y ACCESO (autorización).
// Define la política que exige el rol administrador. La autenticación JWT sigue
// configurada en Program.cs. Sin credenciales la API responde 401 y con un rol
// insuficiente responde 403, así que ambos errores se distinguen (RNF-SEG-03).
public static class AdminAuthorization
{
    public const string PolicyName = "AdminOnly";
    public const string AdminRole = "Admin";

    public static IServiceCollection AddAdminAuthorization(this IServiceCollection services) =>
        services.AddAuthorization(options =>
            options.AddPolicy(PolicyName, policy => policy.RequireRole(AdminRole)));
}
