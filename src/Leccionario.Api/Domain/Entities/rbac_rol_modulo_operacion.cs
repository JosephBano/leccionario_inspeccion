#nullable enable
#pragma warning disable CS8981
﻿using System;
using System.Collections.Generic;

namespace Leccionario.Api.Domain.Entities;

public partial class rbac_rol_modulo_operacion
{
    public int idRolModuloOperacion { get; set; }

    public int idModulosOperaciones { get; set; }

    public int idRol { get; set; }

    public DateOnly? fecha_asignacion { get; set; }

    public DateOnly? fecha_modificacion { get; set; }

    public DateOnly? fecha_desactivacion { get; set; }

    public sbyte? esActivo { get; set; }

    public virtual rbac_modulos_operaciones idModulosOperacionesNavigation { get; set; } = null!;

    public virtual rbac_rol idRolNavigation { get; set; } = null!;
}
