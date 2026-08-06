#nullable enable
#pragma warning disable CS8981
﻿using System;
using System.Collections.Generic;

namespace Leccionario.Api.Domain.Entities;

public partial class fechas_horarios
{
    public int idFecha { get; set; }

    public DateOnly? fecha { get; set; }

    public sbyte? finsemana { get; set; }

    public string? dia { get; set; }

    public virtual ICollection<cplec_sesiones> cplec_sesiones { get; set; } = new List<cplec_sesiones>();
}
