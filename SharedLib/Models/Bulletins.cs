using System;
using System.Collections.Generic;

namespace SharedLib.Models;

public partial class Bulletins
{
    public int Id { get; set; }

    public int RecipientId { get; set; }

    public int ProductType { get; set; }

    public string NumeroContoCorrente { get; set; } = null!;

    public string IBAN { get; set; } = null!;
    public string IntestatoA { get; set; } = null!;

    public string ImportoEuro { get; set; } = null!;

    public string EseguitoDaNominativo { get; set; } = null!;

    public string EseguitoDaIndirizzo { get; set; } = null!;

    public string EseguitoDaLocalita { get; set; } = null!;

    public string AnnoDiRiferimento { get; set; } = null!;

    public string EseguitoDaCap { get; set; } = null!;

    public string CodiceCliente { get; set; } = null!;

    public string? Causale { get; set; }

    public DateOnly? PaymentDate { get; set; }

    public bool? Paid { get; set; }

    public virtual Recipients Recipient { get; set; } = null!;
}
