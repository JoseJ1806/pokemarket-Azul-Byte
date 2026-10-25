namespace PokeArena.Api.Models;

// Habilidad seleccionable al crear una carta.
// HAB-000 representa Sin Habilidad y siempre existe desde el inicio.
public class Ability
{
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Trigger { get; set; }

    public string? Effect { get; set; }

    // Tipo de carta al que se limita la habilidad, si aplica.
    public string? AffectedType { get; set; }

    public bool IsEvolution { get; set; }

    // Código de la carta destino cuando la habilidad es de evolución.
    public string? TargetCardCode { get; set; }
}