using System;
using System.Collections.Generic;

namespace SharedLib.Models;

public partial class Senders
{
    public int Id { get; set; }

    public int OperationId { get; set; }

    public string BusinessName { get; set; } = null!;

    public string? ComplementNames { get; set; }

    public string Address { get; set; } = null!;

    public string? ComplementAddress { get; set; }

    public string ZipCode { get; set; } = null!;

    public string City { get; set; } = null!;

    public string Province { get; set; } = null!;

    public string State { get; set; } = null!;

    public string? Email { get; set; }

    public string? Mobile { get; set; }

    public bool? Ar { get; set; }

    public virtual Operations Operation { get; set; } = null!;
}
