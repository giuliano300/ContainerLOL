using System;
using System.Collections.Generic;

namespace SharedLib.Models;

public partial class Users
{
    public int Id { get; set; }

    public int ParentId { get; set; }

    public int UserTypes { get; set; }

    public Guid Guid { get; set; }

    public string BusinessName { get; set; } = null!;

    public string VatNumber { get; set; } = null!;

    public string Email { get; set; } = null!;

    public string? Mobile { get; set; }

    public string? Province { get; set; }

    public string Password { get; set; } = null!;

    public bool Enabled { get; set; }

    public bool Deleted { get; set; }

    public string Address { get; set; } = null!;

    public string City { get; set; } = null!;

    public string ZipCode { get; set; } = null!;

    public string Pec { get; set; } = null!;

    public string UsernamePoste { get; set; } = null!;

    public string PasswordPoste { get; set; } = null!;

    public string? ArraySenderId { get; set; }

    public virtual ICollection<UserLogos> UserLogos { get; set; } = new List<UserLogos>();

    public virtual ICollection<UserOptions> UserOptions { get; set; } = new List<UserOptions>();

    public virtual ICollection<UserProducts> UserProducts { get; set; } = new List<UserProducts>();

    public virtual ICollection<UserRecipients> UserRecipients { get; set; } = new List<UserRecipients>();

    public virtual ICollection<UserSenders> UserSenders { get; set; } = new List<UserSenders>();
}
