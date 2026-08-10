using System.Data;
using Leccionario.Api.Infrastructure.DbContexts;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace Leccionario.Api.Application.Horarios.Services;

/// <summary>
/// Envuelve validar + escribir en una transacción <c>Serializable</c> de alcance
/// corto, con reintento ante deadlock. Ver <c>ADR-008</c> decisión 6.
/// </summary>
public interface IEscrituraSerializable
{
    Task<T> EjecutarAsync<T>(Func<CancellationToken, Task<T>> operacion, CancellationToken ct = default);
}

/// <inheritdoc cref="IEscrituraSerializable"/>
/// <remarks>
/// <para>Cómo protege, que conviene entender antes de tocar esto: bajo
/// <c>Serializable</c>, InnoDB toma next-key locks sobre el rango leído de
/// <c>ix_horario_detalle_fecha_hora_activo</c>. Dos escrituras concurrentes de la
/// misma celda se bloquean, una muere con el error 1213 y el reintento la repite
/// viendo ya la fila de la otra.</para>
/// <para>Los locks son del motor, así que esto también protege contra
/// <c>gestion_academica</c> escribiendo al mismo tiempo en la misma tabla.</para>
/// <para>El alcance debe ser <b>corto</b>: validar y escribir, nada más. Meter
/// lecturas de catálogo o llamadas lentas acá multiplica los deadlocks.</para>
/// <para>Con EF InMemory las transacciones son no-op, así que en tests unitarios
/// se sustituye por un doble que solo invoca la operación.</para>
/// </remarks>
public sealed class EscrituraSerializable : IEscrituraSerializable
{
    /// <summary>ER_LOCK_DEADLOCK.</summary>
    private const int MySqlDeadlock = 1213;
    /// <summary>ER_LOCK_WAIT_TIMEOUT.</summary>
    private const int MySqlLockWaitTimeout = 1205;

    private readonly sigafi_esContext _db;

    public EscrituraSerializable(sigafi_esContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public Task<T> EjecutarAsync<T>(Func<CancellationToken, Task<T>> operacion, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(operacion);

        return PoliticaReintento.EjecutarAsync(
            async (_, token) =>
            {
                var strategy = _db.Database.CreateExecutionStrategy();
                return await strategy.ExecuteAsync(async () =>
                {
                    await using var tx = await _db.Database
                        .BeginTransactionAsync(IsolationLevel.Serializable, token);

                    var resultado = await operacion(token);

                    await tx.CommitAsync(token);
                    return resultado;
                });
            },
            EsTransitorio,
            ct: ct);
    }

    /// <summary>
    /// Solo deadlock y timeout de lock se reintentan. Un conflicto de negocio o
    /// una violación de FK se propagan: repetirlos daría el mismo error.
    /// </summary>
    private static bool EsTransitorio(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (e is MySqlException { Number: MySqlDeadlock or MySqlLockWaitTimeout })
                return true;
        }
        return false;
    }
}
