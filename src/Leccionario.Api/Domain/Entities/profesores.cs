#nullable enable
#pragma warning disable CS8981
﻿using System;
using System.Collections.Generic;

namespace Leccionario.Api.Domain.Entities;

public partial class profesores
{
    public string idProfesor { get; set; } = null!;

    public string? tipodocumento { get; set; }

    public string? apellidos { get; set; }

    public string? nombres { get; set; }

    public string? primerApellido { get; set; }

    public string? segundoApellido { get; set; }

    public string? primerNombre { get; set; }

    public string? segundoNombre { get; set; }

    public int estadoCivil { get; set; }

    public string? direccion { get; set; }

    public string? callePrincipal { get; set; }

    public string? calleSecundaria { get; set; }

    public string? numeroCasa { get; set; }

    public string? telefono { get; set; }

    public string? celular { get; set; }

    public string? email { get; set; }

    public DateOnly? fecha_nacimiento { get; set; }

    public string? sexo { get; set; }

    public string? clave { get; set; }

    public sbyte? practicas { get; set; }

    public string? tipo { get; set; }

    public string? nacionalidad { get; set; }

    public string? titulo { get; set; }

    public string? abreviatura { get; set; }

    public string? abreviatura_post { get; set; }

    public sbyte? activo { get; set; }

    public int idEtnia { get; set; }

    public int idNacionalidad { get; set; }

    public int idParroquiaNacimiento { get; set; }

    public string? emailInstitucional { get; set; }

    public DateOnly? fecha_ingreso { get; set; }

    public DateOnly? fechaIngresoIess { get; set; }

    public DateOnly? fecha_retiro { get; set; }

    public int idParroquiaResidencia { get; set; }

    public string tipoSangre { get; set; } = null!;

    public string? codigoPostal { get; set; }

    public int idDiscapacidad { get; set; }

    public int? porcentajeDiscapacidad { get; set; }

    public string? numeroConadis { get; set; }

    public string? foto { get; set; }

    public sbyte? esReal { get; set; }
}
