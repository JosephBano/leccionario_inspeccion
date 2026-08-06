#nullable enable
#pragma warning disable CS8981
﻿using System;
using System.Collections.Generic;

namespace Leccionario.Api.Domain.Entities;

public partial class rbac_operaciones
{
    public int idOperaciones { get; set; }

    public string? NombreOperacion { get; set; }

    public virtual ICollection<rbac_modulos_operaciones> rbac_modulos_operaciones { get; set; } = new List<rbac_modulos_operaciones>();
}
