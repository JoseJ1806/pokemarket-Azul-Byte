namespace PokeArena.Api.Modules.Identity.Domain;

// IDENTIDAD EXTERNA (módulo Identidad y Acceso, capa de dominio).
//
// Qué es: los datos de una persona que un proveedor de identidad (Google en el MVP)
// ya verificó. Es un OBJETO DE VALOR: no tiene Id propio, no cambia después de creado
// y dos identidades con los mismos datos se consideran iguales (por eso es un record).
//
// De dónde sale: GoogleIdentityProvider lo arma después de comprobar la firma, el
// emisor, la audiencia y la expiración del ID token. Si el token no pasa esas
// comprobaciones, este objeto nunca se crea.
//
// Quién lo usa: SignInWithProviderUseCase, para buscar la cuenta del jugador, y
// PlayerAccount.CreatePlayer, para crearla en el primer ingreso.
//
// Campos:
// Provider: nombre del proveedor, por ejemplo "Google". Permite que en el futuro el
//           mismo identificador exista en dos proveedores distintos sin chocar.
// Subject:  identificador ÚNICO Y PERMANENTE de la persona dentro del proveedor (el
//           claim "sub" del token). Google nunca lo cambia ni lo reutiliza.
// Email:    correo de la persona. Solo se guarda para mostrarlo. NO identifica la
//           cuenta, porque la persona puede cambiarlo en Google (criterio alterno de
//           la US: si cambia el correo, conserva su cuenta en PokéArena).
public sealed record ExternalIdentity(string Provider, string Subject, string Email);
