#nullable enable
#pragma warning disable CS8981
﻿using System;
using System.Collections.Generic;

namespace Leccionario.Api.Domain.Entities;

public partial class periodos
{
    public string idPeriodo { get; set; } = null!;

    public string? detalle { get; set; }

    public DateOnly? fecha_inicial { get; set; }

    public DateOnly? fecha_final { get; set; }

    public bool? cerrado { get; set; }

    public DateOnly? fecha_maxima_autocierre { get; set; }

    public bool? activo { get; set; }

    public bool? creditos { get; set; }

    public uint? numero_pagos { get; set; }

    public DateOnly? fecha_matrucla_extraordinaria { get; set; }

    public int? foliop { get; set; }

    public sbyte? permiteMatricula { get; set; }

    public sbyte? ingresoCalificaciones { get; set; }

    public sbyte? permiteCalificacionesInstituto { get; set; }

    public sbyte? periodoactivoinstituto { get; set; }

    public sbyte? visualizaPowerBi { get; set; }

    public sbyte? esInstituto { get; set; }

    public sbyte? periodoPlanificacion { get; set; }

    public sbyte? esConduccion { get; set; }

    public virtual ICollection<matriculas> matriculas { get; set; } = new List<matriculas>();
}
