namespace PokeArena.Api.Models;

// MODELO DE DOMINIO DE CARTA.
// Program.cs usa esta clase al proyectar los resultados de GET /api/cards.
// PokeArenaDbContext la relaciona con la tabla cards de PostgreSQL.
public class Card
{
    // Identificador interno generado por la base de datos.
    public int Id { get; set; }

    // Código público usado para identificar y buscar la carta.
    public string Code { get; set; } = string.Empty;

    // Nombre visible de la carta; admite búsquedas parciales.
    public string Name { get; set; } = string.Empty;

    // Dirección de la imagen; puede estar vacía mientras se carga la imagen.
    public string? ImageUrl { get; set; }

    // Estado de procesamiento de la imagen, por ejemplo Pendiente o Lista.
    public string ImageStatus { get; set; } = "Pendiente";

    // Costo de uso de la carta.
    public int Cost { get; set; }

    // Poder de la carta.
    public int Power { get; set; }

    // Tipo ingresado manualmente por el administrador.
    public string Type { get; set; } = string.Empty;

    // Rareza ingresada manualmente por el administrador.
    public string Rarity { get; set; } = string.Empty;

    // Datos opcionales de la habilidad; null representa Sin Habilidad.
    public string? AbilityCode { get; set; }

    // Evento que activa la habilidad.
    public string? AbilityTrigger { get; set; }

    // Efecto que produce la habilidad.
    public string? AbilityEffect { get; set; }
}