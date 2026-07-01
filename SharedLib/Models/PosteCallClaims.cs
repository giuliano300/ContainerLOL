using System;

namespace SharedLib.Models;

public partial class PosteCallClaims
{
    public Guid Id { get; set; }

    public int RecipientId { get; set; }

    public int Step { get; set; }

    public DateTime CreatedAt { get; set; }

    public string? Message { get; set; }

    public virtual Recipients Recipient { get; set; } = null!;

    /// <summary>
    /// Initializes a claim with local defaults matching the database defaults.
    /// </summary>
    public PosteCallClaims()
    {
        Id = Guid.NewGuid();
        CreatedAt = DateTime.UtcNow;
    }
}
