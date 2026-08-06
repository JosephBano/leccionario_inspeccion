#nullable enable
#pragma warning disable CS8981
﻿using System;
using System.Collections.Generic;

namespace Leccionario.Api.Domain.Entities;

public partial class carreras
{
    public int idCarrera { get; set; }

    public string? Carrera { get; set; }

    public DateOnly? fechaCreacion { get; set; }

    public bool? activa { get; set; }

    public string? directorCarrera { get; set; }

    public int? numero_creditos { get; set; }

    public int? ordenCarrera { get; set; }

    public int? numero_alumnos { get; set; }

    public sbyte? revisaArrastres { get; set; }

    public string? codigo_cases { get; set; }

    public string? aliasCarrera { get; set; }

    public bool? BolsaEmpleo { get; set; }

    public sbyte? esInstituto { get; set; }

    public virtual ICollection<cursos> cursos { get; set; } = new List<cursos>();
}
