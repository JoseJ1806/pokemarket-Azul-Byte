namespace PokeArena.Api.Modules.Identity.Domain;

// ROLES DEL SISTEMA (módulo Identidad y Acceso, capa de dominio).
//
// Qué es: los nombres exactos de los roles que viajan dentro del JWT de PokéArena,
// en el claim "role".
//
// Por qué existe: para no escribir "Player" o "Admin" como texto suelto en varios
// archivos. Si alguien escribe mal un rol, el compilador no lo detecta, pero si todos
// usan estas constantes, el error es imposible.
//
// Quién lo usa:
// 1. PlayerAccount.CreatePlayer asigna Player a toda cuenta nueva.
// 2. JwtAccessTokenIssuer escribe el rol de la cuenta dentro del token.
// 3. AdminAuthorization ya tenía su propia constante "Admin" desde el Sprint 1. Aquí
//    se reutiliza esa misma constante para que exista una sola fuente.
public static class IdentityRoles
{
    // Rol de administrador. Puede crear y buscar cartas y ubicaciones.
    // Se toma de AdminAuthorization para no duplicar el texto "Admin".
    public const string Admin = AdminAuthorization.AdminRole;

    // Rol de jugador. Es el que recibe cualquier persona que inicia sesión con Google
    // por primera vez (criterio de éxito de la US Inicio de sesión con proveedor).
    public const string Player = "Player";
}
