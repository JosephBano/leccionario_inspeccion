#nullable enable
#pragma warning disable CS8981
﻿using System;
using System.Collections.Generic;

namespace Leccionario.Api.Domain.Entities;

public partial class modalidades
{
    public int idModalidad { get; set; }

    public string? modalidad { get; set; }

    public string? sufijo { get; set; }

    public string? modalidadImpresion { get; set; }

    public virtual ICollection<matriculas> matriculas { get; set; } = new List<matriculas>();
}
