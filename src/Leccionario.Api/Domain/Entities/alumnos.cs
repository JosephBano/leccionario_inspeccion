#nullable enable
#pragma warning disable CS8981
﻿using System;
using System.Collections.Generic;

namespace Leccionario.Api.Domain.Entities;

public partial class alumnos
{
    public string idAlumno { get; set; } = null!;

    public string? tipoDocumento { get; set; }

    public string? apellidoPaterno { get; set; }

    public string? apellidoMaterno { get; set; }

    public string? primerNombre { get; set; }

    public string? segundoNombre { get; set; }

    public DateOnly? fecha_Nacimiento { get; set; }

    public string? direccion { get; set; }

    public string? telefono { get; set; }

    public string? celular { get; set; }

    public string? email { get; set; }

    public string? ciudad_Nacimiento { get; set; }

    public string? provincia_Nacimiento { get; set; }

    public byte[]? foto { get; set; }

    public string? sexo { get; set; }

    public string? nacionalidad { get; set; }

    public int? idNivel { get; set; }

    public string? idPeriodo { get; set; }

    public int? idSeccion { get; set; }

    public int? idModalidad { get; set; }

    public int? idInstitucion { get; set; }

    public string? tituloColegio { get; set; }

    public DateTime? fecha_Inscripcion { get; set; }

    public string? parroquia_nacimiento { get; set; }

    public string? nombre_padre { get; set; }

    public string? ocupacion_padre { get; set; }

    public string? nacionalidad_padre { get; set; }

    public string? nombre_madre { get; set; }

    public string? ocupacion_madre { get; set; }

    public string? nacionalidad_madre { get; set; }

    public string? barrio_residencia { get; set; }

    public string? parroquia_residencia { get; set; }

    public string? ciudad_residencia { get; set; }

    public string? tipo_sangre { get; set; }

    public string? user_alumno { get; set; }

    public string? password { get; set; }

    public int? idDiscapacidad { get; set; }

    public int? idEtnia { get; set; }

    public int? idNacionalidad { get; set; }

    public int? porcentaje_discapacidad { get; set; }

    public string? carnet_conadis { get; set; }

    public string? email_institucional { get; set; }

    public sbyte? primerIngreso { get; set; }

    public string? archivofoto { get; set; }

    public sbyte? fotoAprobada { get; set; }

    public int? IdEstadoCivil { get; set; }

    public int? idGeneroAlumno { get; set; }

    public int? idNacionalidadEtnica { get; set; }

    public int? idParroquiaResidencia { get; set; }

    public string? tipoInstitucion { get; set; }

    public virtual ICollection<matriculas> matriculas { get; set; } = new List<matriculas>();
}
