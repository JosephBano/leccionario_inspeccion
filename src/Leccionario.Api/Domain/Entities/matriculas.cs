#nullable enable
#pragma warning disable CS8981
﻿using System;
using System.Collections.Generic;

namespace Leccionario.Api.Domain.Entities;

public partial class matriculas
{
    public int idMatricula { get; set; }

    public string idAlumno { get; set; } = null!;

    public int idNivel { get; set; }

    public int idSeccion { get; set; }

    public int idModalidad { get; set; }

    public string idPeriodo { get; set; } = null!;

    public DateTime? fechaMatricula { get; set; }

    public string? paralelo { get; set; }

    public bool? arrastres { get; set; }

    public int? folio { get; set; }

    public decimal? beca_matricula { get; set; }

    public decimal? beca_colegiatura { get; set; }

    public bool? retirado { get; set; }

    public DateOnly? fechaRetiro { get; set; }

    public string? observacion { get; set; }

    public bool? convalidacion { get; set; }

    public string? carrera_convalidada { get; set; }

    public int? numero_permiso { get; set; }

    public string? user_matricula { get; set; }

    public sbyte? valida { get; set; }

    public sbyte? esOyente { get; set; }

    public string? documentoFactura { get; set; }

    public virtual ICollection<cplec_asistencias> cplec_asistencias { get; set; } = new List<cplec_asistencias>();

    public virtual alumnos idAlumnoNavigation { get; set; } = null!;

    public virtual modalidades idModalidadNavigation { get; set; } = null!;

    public virtual cursos idNivelNavigation { get; set; } = null!;

    public virtual periodos idPeriodoNavigation { get; set; } = null!;

    public virtual secciones idSeccionNavigation { get; set; } = null!;
}
