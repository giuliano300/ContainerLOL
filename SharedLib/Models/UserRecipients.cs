using System;
using System.Collections.Generic;

namespace SharedLib.Models;

public partial class UserRecipients
{
    public int Id { get; set; }

    public string BusinessName { get; set; } = null!;

    public string? ComplementNames { get; set; }

    public string Address { get; set; } = null!;

    public string? ComplementAddress { get; set; }

    public string ZipCode { get; set; } = null!;

    public string City { get; set; } = null!;

    public string Province { get; set; } = null!;

    public string State { get; set; } = null!;

    public int UserId { get; set; }

    public int UserParentId { get; set; }

    public string? Email { get; set; }

    public string? Mobile { get; set; }

    public string? FiscalCode { get; set; }

    public virtual Users User { get; set; } = null!;
}
