using SharedLib.Db;
using SharedLib.Models;
using ServiceReference;
using System.Security.Cryptography;
using SharedLib.Config;

namespace SharedLib.Utils;

public static class LOLServiceHelper
{
	public static LOLServiceSoapClient GetNewServiceLOL(Users user, LolServiceOptions options)
    {
		var mode = options.SecurityMode.ToLowerInvariant() switch
		{
			"none" => System.ServiceModel.BasicHttpSecurityMode.None,
			"message" => System.ServiceModel.BasicHttpSecurityMode.Message,
			"transportwithmessagecredential" => System.ServiceModel.BasicHttpSecurityMode.TransportWithMessageCredential,
			_ => System.ServiceModel.BasicHttpSecurityMode.Transport
		};

		var binding = new System.ServiceModel.BasicHttpBinding(mode)
		{
			Security = { 
				Transport = { 
					ClientCredentialType = System.ServiceModel.HttpClientCredentialType.Basic 
				} 
			},
			MaxReceivedMessageSize = 10 * 1024 * 1024
		};

		var endpoint = new System.ServiceModel.EndpointAddress(options.Url);
		var client = new LOLServiceSoapClient(binding, endpoint);
		client.ClientCredentials.UserName.UserName = user.UsernamePoste;
		client.ClientCredentials.UserName.Password = user.PasswordPoste;

		return client; 

	}

	public static string? GetRequestId(Guid Guid, AppDbContext db, LolServiceOptions options, LOLServiceSoapClient client)
	{
		var user = db.Users.FirstOrDefault(u => u.Guid == Guid);
		if (user == null) 
			return null;

		var result = client.RecuperaIdRichiestaAsync().Result;

		return result.IDRichiesta;
	}

	public static Mittente GetMittente(Senders sender)
	{
		return new Mittente
		{
			Nominativo = new Nominativo
			{
				Nome = string.Empty,
				Cognome = string.Empty,
				RagioneSociale = sender.BusinessName,
				ComplementoNominativo = sender.ComplementNames,
				CAP = sender.ZipCode,
				Citta = sender.City,
				Provincia = sender.Province,
				Telefono = sender.Mobile,
				CasellaPostale = string.Empty,
				UfficioPostale = string.Empty,
				Zona = string.Empty,
				Stato = sender.State,
				TipoIndirizzo = NominativoTipoIndirizzo.NORMALE,
				Indirizzo = new Indirizzo
				{
					NumeroCivico = string.Empty,
					Toponimo = sender.Address,
					DUG = string.Empty,
					Esponente = string.Empty,
				},
				ComplementoIndirizzo = sender.ComplementAddress,
				Frazione = string.Empty
			},
			InviaStampa = false
		};
	}

	private static OpzionidiStampa GetOpzioniDiStampa(int printType, int frontBack)
	{
		return new OpzionidiStampa
		{
			BW = printType == (int)PrintType.BiancoNero ? "true" : "false",
			FronteRetro = frontBack == (int)FrontBack.SoloFronte ? "false" : "true"
		};
	}

	public static LOLSubmitOpzioni GetOpzioniLOL(int PrintType, int FrontBack)
	{

		return new LOLSubmitOpzioni()
		{
			Archiviazione = false,
			DataStampa = DateTime.Now,
			DPM = false,
			OpzionidiStampa = GetOpzioniDiStampa(PrintType, FrontBack),
			FirmaElettronica = false,
			InserisciMittente = false,
			Inserti = new LOLSubmitOpzioniInserti()
			{
				InserisciMittente = false,
				Inserto = string.Empty
			}
		};
	}

	public static Destinatario GetDestinatarioLOL(Recipients n)
	{
		return new Destinatario
		{
			Nominativo = new Nominativo
			{
				Nome = string.Empty,
				Cognome = string.Empty,
				RagioneSociale = n.BusinessName,
				ComplementoNominativo = n.ComplementName,
				CAP = n.ZipCode,
				Citta = n.City,
				Provincia = n.Province,
				Stato = n.State,
				CodiceFiscale = n.FiscalCode,
				Telefono = n.Mobile,
				UfficioPostale = string.Empty,
				Zona = string.Empty,
				CasellaPostale = string.Empty,
				Frazione = string.Empty,

				TipoIndirizzo = NominativoTipoIndirizzo.NORMALE,
				Indirizzo = new Indirizzo
				{
					Toponimo = n.Address,
					DUG = string.Empty,
					NumeroCivico = string.Empty,
					Esponente = string.Empty
				},
				ComplementoIndirizzo = n.ComplementAddress,
			},
			IdDestinatario = Convert.ToString(1),
			IdRicevuta = String.Empty
	};
	}

	public static Documento[] GetDoc(byte[] AttachedFile)
	{
		using var md5 = MD5.Create();
		Documento[] Documenti = new Documento[1];
		Documento documento = new Documento()
		{
			Immagine = AttachedFile,
			MD5 = BitConverter.ToString(md5.ComputeHash(AttachedFile)).Replace("-", string.Empty),
			TipoDocumento = "pdf"
		};

		Documenti[0] = documento;
		return Documenti;
	}

    public static Bollettino896 GetBollettino896(Bulletins bollettino)
    {
        Bollettino896 b = new Bollettino896();
        b.NumeroContoCorrente = bollettino.NumeroContoCorrente;
        b.IntestatoA = bollettino.IntestatoA;
        b.FormatoStampa = 0;
        b.AdditionalInfo = "";
        b.IBAN = bollettino.IBAN;
        b.EseguitoDa = new BollettinoEseguitoDa();
        b.EseguitoDa.Nominativo = bollettino.EseguitoDaNominativo;
        b.EseguitoDa.Indirizzo = bollettino.EseguitoDaIndirizzo;
        b.EseguitoDa.CAP = bollettino.EseguitoDaCap;
        b.EseguitoDa.Localita = bollettino.EseguitoDaLocalita;
        b.CodiceCliente = bollettino.CodiceCliente;
        b.ImportoEuro = Convert.ToDecimal(bollettino.ImportoEuro);
        b.Causale = bollettino.Causale;
        return b;
    }
}
