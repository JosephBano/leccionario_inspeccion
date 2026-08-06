namespace Leccionario.Api.Application.Common.Exceptions;

/// <summary>403 FUERA_DE_PLAZO — el docente intenta editar fuera de la ventana configurada.</summary>
public sealed class FueraDePlazoException : ProhibidoException
{
    public FueraDePlazoException(int ventanaHoras)
        : base("FUERA_DE_PLAZO", $"La edición está permitida solo dentro de las {ventanaHoras} horas posteriores al registro.")
    {
        VentanaHoras = ventanaHoras;
    }

    public int VentanaHoras { get; }
}
