#nullable enable
#pragma warning disable CS8981
using System;
using System.Collections.Generic;

namespace Leccionario.Api.Domain.Entities;

public partial class horario_detalle
{
    public int idHorario { get; set; }

    public int idAsignacion { get; set; }

    public int idFecha { get; set; }

    public int idhora { get; set; }

    public int? idEspacio { get; set; }

    public string? tipoBloque { get; set; }

    public sbyte? activo { get; set; }

    public sbyte? claseReasignacion { get; set; }

    public bool? esRecuperacionPedagocia { get; set; }

    public string? observacion { get; set; }

    public int? idHorarioReasgincacion { get; set; }

    public virtual espacios? idEspacioNavigation { get; set; }
}
