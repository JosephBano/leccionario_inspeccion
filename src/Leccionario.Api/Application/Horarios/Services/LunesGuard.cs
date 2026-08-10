using Leccionario.Api.Application.Common.Exceptions;

namespace Leccionario.Api.Application.Horarios.Services;

/// <summary>
/// Garantiza que las fechas que representan "inicio de semana" sean realmente lunes.
/// </summary>
/// <remarks>
/// <para>
/// El frontend envía la fecha como <c>yyyy-MM-dd</c> asumiendo que es lunes; el
/// backend no debe fiarse. Si llega cualquier otro día de la semana, el grid
/// devuelve etiquetas desalineadas: <c>HorarioService</c> arma los siete días
/// con <c>lunes.AddDays(0..6)</c> y los etiqueta por índice sobre
/// <c>["Lunes", "Martes", …]</c>, así que "martes" aparecería como "Lunes"
/// silenciosamente.
/// </para>
/// <para>
/// Defensa en profundidad del lado backend para
/// <see cref="IHorarioService.ObtenerGridAsync"/>. Si vuelve a aparecer un
/// desfase de zona horaria en el cliente, este guard lo convierte en un 400
/// <c>VALIDACION</c> explícito en vez de un dato mal catalogado.
/// </para>
/// <para>
/// No "arregla" la fecha moviéndola al lunes anterior: si el cliente manda
/// martes, es bug del cliente y debe verse. Sin fallback silencioso.
/// </para>
/// </remarks>
/// <seealso cref="docs/adr/ADR-009-fechas-calendario-helper-y-guard.md"/>
public interface ILunesGuard
{
    /// <summary>
    /// Lanza <see cref="ValidacionException"/> si <paramref name="lunes"/>
    /// no es lunes.
    /// </summary>
    void EnsureEsLunes(DateOnly lunes);
}

/// <inheritdoc cref="ILunesGuard"/>
public sealed class LunesGuard : ILunesGuard
{
    public void EnsureEsLunes(DateOnly lunes)
    {
        if (lunes.DayOfWeek != DayOfWeek.Monday)
        {
            throw new ValidacionException(
                $"La fecha '{lunes:yyyy-MM-dd}' no es lunes. El parámetro 'lunes' debe ser un lunes.");
        }
    }
}
