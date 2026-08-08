using Leccionario.Api.Application.Horarios.Services;

namespace Leccionario.Tests.Horarios;

/// <summary>
/// Doble de <see cref="IEscrituraSerializable"/> para tests unitarios: ejecuta la
/// operación sin transacción.
/// </summary>
/// <remarks>
/// EF InMemory no soporta transacciones ni niveles de aislamiento, así que la
/// transacción real no se puede ejercitar acá. La lógica de reintento se prueba
/// aparte en <c>PoliticaReintentoTests</c>, y el cableado con MySQL en integración.
/// </remarks>
internal sealed class EscrituraDirecta : IEscrituraSerializable
{
    public Task<T> EjecutarAsync<T>(Func<CancellationToken, Task<T>> operacion, CancellationToken ct = default) =>
        operacion(ct);
}
