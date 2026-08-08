#nullable enable
#pragma warning disable CS8981
using System;
using System.Collections.Generic;

namespace Leccionario.Api.Domain.Entities;

public partial class horas_clases
{
    public int idhora { get; set; }

    public int? idSeccion { get; set; }

    public int? idCarrera { get; set; }

    public string? hora_inicio { get; set; }

    public string? hora_fin { get; set; }

    public int? minutos { get; set; }

    public int? numero_hora { get; set; }

    public string? tipo { get; set; }

    public sbyte? activo { get; set; }

    public virtual ICollection<horario_detalle> horario_detalle { get; set; } = new List<horario_detalle>();
}
