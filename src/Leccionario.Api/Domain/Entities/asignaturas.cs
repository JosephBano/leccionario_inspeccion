#nullable enable
#pragma warning disable CS8981
﻿using System;
using System.Collections.Generic;

namespace Leccionario.Api.Domain.Entities;

public partial class asignaturas
{
    public int idAsignatura { get; set; }

    public string? asignatura { get; set; }

    public bool? anulada { get; set; }

    public string? codigo { get; set; }

    public sbyte? extraCurricular { get; set; }
}
