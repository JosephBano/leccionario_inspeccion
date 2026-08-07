namespace Leccionario.Api.Application.Common.Exceptions;

/// <summary>422 FUERA_DE_VENTANA — la fecha de la sesión cae fuera de la ventana de la asignación.</summary>
public sealed class FueraDeVentanaException : AppException
{
    public DateOnly? FechaInicial { get; }
    public DateOnly? FechaFin { get; }

    public FueraDeVentanaException(DateOnly? fechaInicial = null, DateOnly? fechaFin = null)
        : base("FUERA_DE_VENTANA", 422, "La fecha cae fuera del rango válido de la asignación.")
    {
        FechaInicial = fechaInicial;
        FechaFin = fechaFin;
    }
}