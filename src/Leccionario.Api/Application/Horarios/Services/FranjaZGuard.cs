using Leccionario.Api.Application.Common.Exceptions;
using Leccionario.Api.Infrastructure.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Leccionario.Api.Application.Horarios.Services;

/// <summary>
/// Frontera de cplec sobre el catálogo compartido `horas_clases`: solo se crean,
/// editan o desactivan franjas <c>tipo = 'Z'</c>. Ver <c>ADR-008</c> decisión 3.
/// </summary>
public interface IFranjaZGuard
{
    Task<bool> EsFranjaZAsync(int idhora, CancellationToken ct = default);

    /// <summary>Lanza <see cref="FranjaNoPropiaException"/> si la franja no es de cplec.</summary>
    Task EnsureEsFranjaZAsync(int idhora, CancellationToken ct = default);
}

/// <inheritdoc cref="IFranjaZGuard"/>
/// <remarks>
/// <para>Tipos presentes en la base (verificado 2026-08-07): <c>C</c> 54 filas,
/// <c>I</c> 7, <c>X</c> 11. <c>Z</c> estaba libre y queda reservado para cplec.</para>
/// <para>Este guard aplica solo a la <b>escritura</b>. La detección de conflictos
/// lee todos los tipos a propósito: un docente ocupado en una franja <c>X</c>
/// sigue estando ocupado.</para>
/// <para>Franja inexistente o con <c>tipo</c> NULL: se niega. No se asume
/// propiedad sobre una fila que no podemos identificar como nuestra.</para>
/// </remarks>
public sealed class FranjaZGuard : IFranjaZGuard
{
    /// <summary>Tipo de franja reservado para cplec.</summary>
    public const string TipoCplec = "Z";

    private readonly sigafi_esContext _db;

    public FranjaZGuard(sigafi_esContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public Task<bool> EsFranjaZAsync(int idhora, CancellationToken ct = default) =>
        _db.horas_clases
            .AsNoTracking()
            .AnyAsync(h => h.idhora == idhora && h.tipo == TipoCplec, ct);

    public async Task EnsureEsFranjaZAsync(int idhora, CancellationToken ct = default)
    {
        if (!await EsFranjaZAsync(idhora, ct))
            throw new FranjaNoPropiaException();
    }
}
