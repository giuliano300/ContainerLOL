using System;
using System.Collections.Generic;

namespace SharedLib.Models;

public partial class PasswordRecovery
{
    public int Id { get; set; }

    public string Email { get; set; } = null!;

    public string Token { get; set; } = null!;

    public bool Used { get; set; }

    public DateTime ExpirationDate { get; set; }
}
