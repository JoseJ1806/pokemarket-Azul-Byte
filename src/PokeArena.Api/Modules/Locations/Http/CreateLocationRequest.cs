namespace PokeArena.Api.Modules.Locations.Http;

// Campos del formulario multipart/form-data de POST /api/locations.
// CardLimit y EnergyAmount son int? para distinguir "no enviado" de 0, que es un valor válido.
public sealed class CreateLocationRequest
{
    public string? Name { get; set; }

    public string? Description { get; set; }

    public int? CardLimit { get; set; }

    public string? EnergyMode { get; set; }

    public int? EnergyAmount { get; set; }

    public string? EffectType { get; set; }

    public string? EffectParameters { get; set; }

    public IFormFile? Image { get; set; }
}