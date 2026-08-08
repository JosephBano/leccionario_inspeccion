namespace Leccionario.Api.Application.Horarios.Services;

/// <summary>Una franja del horario de un día, ya parseada a <see cref="TimeOnly"/>.</summary>
public sealed record FranjaOrdenable(
    int IdHorario, int Idhora, TimeOnly Inicio, TimeOnly Fin, int Minutos);

/// <summary>
/// Una corrida de franjas contiguas. Es la unidad a la que se ancla una sesión
/// de clase.
/// </summary>
/// <param name="IdHorarioInicio">
/// Identidad estable del bloque: el <c>idHorario</c> de su primera franja.
/// </param>
public sealed record BloqueHorario(
    int NumeroBloque,
    int IdHorarioInicio,
    TimeOnly Inicio,
    TimeOnly Fin,
    int FranjasPlanificadas,
    int MinutosPlanificados);

/// <summary>
/// Agrupa las franjas de una asignación en un día en bloques contiguos.
/// Ver <c>ADR-008</c> decisión 7.
/// </summary>
/// <remarks>
/// <para>Contigüidad es <b>igualdad exacta</b> de <c>Fin</c> con el <c>Inicio</c>
/// siguiente. No hay tolerancia: un hueco de un minuto es un hueco.</para>
/// <para>Se ordena internamente porque el origen es una consulta sobre
/// <c>hora_inicio varchar(5)</c> y no conviene depender de su orden.</para>
/// <para><c>MinutosPlanificados</c> suma los minutos reales en vez de contar
/// franjas: con franjas Z de duración libre, contar mentiría.</para>
/// </remarks>
public static class BloqueHorarioCalculator
{
    public static IReadOnlyList<BloqueHorario> Agrupar(IEnumerable<FranjaOrdenable> franjas)
    {
        ArgumentNullException.ThrowIfNull(franjas);

        var ordenadas = franjas.OrderBy(f => f.Inicio).ThenBy(f => f.Fin).ToList();
        var bloques = new List<BloqueHorario>();

        var i = 0;
        while (i < ordenadas.Count)
        {
            var primera = ordenadas[i];
            var fin = primera.Fin;
            var cantidad = 1;
            var minutos = primera.Minutos;

            var j = i + 1;
            while (j < ordenadas.Count && ordenadas[j].Inicio == fin)
            {
                fin = ordenadas[j].Fin;
                minutos += ordenadas[j].Minutos;
                cantidad++;
                j++;
            }

            bloques.Add(new BloqueHorario(
                NumeroBloque: bloques.Count + 1,
                IdHorarioInicio: primera.IdHorario,
                Inicio: primera.Inicio,
                Fin: fin,
                FranjasPlanificadas: cantidad,
                MinutosPlanificados: minutos));

            i = j;
        }

        return bloques;
    }
}
