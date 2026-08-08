namespace Leccionario.Api.Application.Horarios.Services;

/// <summary>
/// Reintento acotado ante fallos transitorios. Función pura: sin EF, sin MySQL,
/// sin reloj — para poder testearla exhaustivamente.
/// </summary>
/// <remarks>
/// <para>Existe porque <c>horario_detalle</c> no tiene ningún índice único
/// (spec H1): la unicidad la produce el deadlock bajo aislamiento
/// <c>Serializable</c> más este reintento. Si se elimina, aparecen duplicados.</para>
/// <para>Sin backoff: los deadlocks de InnoDB se resuelven abortando una de las
/// transacciones de inmediato, así que el reintento puede ser inmediato. Esperar
/// solo alargaría el lock de la otra.</para>
/// </remarks>
public static class PoliticaReintento
{
    /// <summary>Intentos totales, incluido el primero.</summary>
    public const int MaxIntentos = 3;

    /// <param name="intento">Recibe el número de intento (1-based) y el token.</param>
    /// <param name="esTransitorio">Decide si la excepción amerita reintento.</param>
    public static async Task<T> EjecutarAsync<T>(
        Func<int, CancellationToken, Task<T>> intento,
        Func<Exception, bool> esTransitorio,
        int maxIntentos = MaxIntentos,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(intento);
        ArgumentNullException.ThrowIfNull(esTransitorio);

        for (var n = 1; ; n++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                return await intento(n, ct);
            }
            catch (Exception ex) when (n < maxIntentos && esTransitorio(ex))
            {
                // Se reintenta. La condición del `when` deja pasar la excepción
                // intacta cuando ya no quedan intentos o no es transitoria.
            }
        }
    }
}
