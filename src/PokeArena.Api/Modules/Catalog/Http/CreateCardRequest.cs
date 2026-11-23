namespace PokeArena.Api.Modules.Catalog.Http;

// Campos del formulario multipart/form-data de POST /api/cards.
// Los nombres coinciden con los campos que envía el cliente.
public sealed class CreateCardRequest
{
    public string? Name { get; set; }

    public int Cost { get; set; }

    public int Power { get; set; }

    public string? Type { get; set; }

    public string? Rarity { get; set; }

    public string? AbilityCode { get; set; }

    public IFormFile? Image { get; set; }
}
