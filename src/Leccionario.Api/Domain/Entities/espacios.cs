#nullable enable
#pragma warning disable CS8981
using System;
using System.Collections.Generic;

namespace Leccionario.Api.Domain.Entities;

public partial class espacios
{
    public int idEspacio { get; set; }

    public string? espacio { get; set; }

    public sbyte? activo { get; set; }

    public virtual ICollection<horario_detalle> horario_detalle { get; set; } = new List<horario_detalle>();
}
