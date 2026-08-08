namespace Leccionario.Api.Application.Common.Exceptions;

/// <summary>
/// 422 SIN_HORARIO — la asignación no tiene ninguna celda de horario activa, así
/// que no puede registrarse asistencia hasta que el inspector la cargue. Retira
/// el "modo transición" de ADR-008 decisión 9. Ver
/// <c>docs/superpowers/specs/2026-08-08-asistencia-anclada-horario-design.md</c> sección 4.1.
/// </summary>
public sealed class SinHorarioException : AppException
{
    public SinHorarioException()
        : base("SIN_HORARIO", 422,
               "Este paralelo no tiene horario planificado. Pide al inspector que lo cargue.") { }
}
