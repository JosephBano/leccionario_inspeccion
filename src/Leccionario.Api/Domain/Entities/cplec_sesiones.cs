#nullable enable
#pragma warning disable CS8981
﻿using System;
using System.Collections.Generic;

namespace Leccionario.Api.Domain.Entities;

/// <summary>
/// cplec — Sesion de clase del leccionario (grano: dia)
/// </summary>
public partial class cplec_sesiones
{
    public int idSesion { get; set; }

    /// <summary>
    /// FK asignaciones_profesores.idAsignacion (columna UNIQUE, no la PK compuesta)
    /// </summary>
    public int idAsignacion { get; set; }

    /// <summary>
    /// FK fechas_horarios.idFecha — el dia de la clase
    /// </summary>
    public int idFecha { get; set; }

    /// <summary>
    /// Orden de la clase dentro del dia. Normalmente 1.
    /// </summary>
    public sbyte numeroBloque { get; set; }

    /// <summary>
    /// Tema general de la clase — el leccionario propiamente dicho
    /// </summary>
    public string tema { get; set; } = null!;

    /// <summary>
    /// Observaciones generales de la sesion
    /// </summary>
    public string? observacion { get; set; }

    /// <summary>
    /// cerrada = congelada; solo un inspector puede reabrirla
    /// </summary>
    public string estado { get; set; } = null!;

    public DateTime? fechaCierre { get; set; }

    public bool? activo { get; set; }

    /// <summary>
    /// usuarios.idSigafi del docente
    /// </summary>
    public string usuarioCreacion { get; set; } = null!;

    public DateTime fechaCreacion { get; set; }

    public string? usuarioActualiza { get; set; }

    public DateTime? fechaActualizacion { get; set; }

    public virtual ICollection<cplec_asistencias> cplec_asistencias { get; set; } = new List<cplec_asistencias>();

    public virtual asignaciones_profesores idAsignacionNavigation { get; set; } = null!;

    public virtual fechas_horarios idFechaNavigation { get; set; } = null!;
}
