#nullable enable
#pragma warning disable CS8981
﻿using System;
using System.Collections.Generic;

namespace Leccionario.Api.Domain.Entities;

public partial class secciones
{
    public int idSeccion { get; set; }

    public string? seccion { get; set; }

    public string? sufijo { get; set; }

    public virtual ICollection<matriculas> matriculas { get; set; } = new List<matriculas>();
}
