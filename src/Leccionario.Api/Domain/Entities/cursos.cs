#nullable enable
#pragma warning disable CS8981
﻿using System;
using System.Collections.Generic;

namespace Leccionario.Api.Domain.Entities;

public partial class cursos
{
    public int idNivel { get; set; }

    public int idCarrera { get; set; }

    public string? Nivel { get; set; }

    public int? jerarquia { get; set; }

    public int? orden { get; set; }

    public sbyte? esRecuperacion { get; set; }

    public string? aliasCurso { get; set; }

    public virtual carreras idCarreraNavigation { get; set; } = null!;

    public virtual ICollection<matriculas> matriculas { get; set; } = new List<matriculas>();
}
