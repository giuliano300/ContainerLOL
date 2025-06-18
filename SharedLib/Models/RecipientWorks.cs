using System;
using System.Collections.Generic;

namespace SharedLib.Models;

public partial class RecipientWorks
{
    public Guid Id { get; set; }

    public int RecipientId { get; set; }

    public int WorkStatus {  get; set; }

    public DateTime WorkDate { get; set; }

    public string? Message { get; set; }

    public virtual Recipients Recipient { get; set; } = null!;

    public RecipientWorks() 
    {
        Id = Guid.NewGuid();    
    }
}
