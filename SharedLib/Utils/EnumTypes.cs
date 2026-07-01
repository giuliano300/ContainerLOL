using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;


namespace SharedLib.Utils
{
public enum ProductTypes
{
	[Display(Name = "Raccomandata Ordinaria")]
	ROL = 1,

	[Display(Name = "Lettera Ordinaria")]
	LOL = 2,

	[Display(Name = "Telegramma Ordinario")]
	TOL = 3,

	[Display(Name = "Raccomandata Market")]
	MOL = 4,

	[Display(Name = "Posta Contest")]
	COL = 5,

	[Display(Name = "Atto Giudiziario")]
	AGOL = 6,

	[Display(Name = "Visura/Certificato")]
	VOL = 7
}

public enum VolTypes
{
	[Display(Name = "Bilancio completo (BICM)")]
	BilancioCompleto = 5,

	[Display(Name = "Fascicolo completo (FASC)")]
	FascicoloCompleto = 6,

	[Display(Name = "Ricerca protesti (RIPR)")]
	RicercaProtesti = 7,

	[Display(Name = "Scheda persona (SCPE)")]
	SchedaPersona = 8,

	[Display(Name = "Scheda socio (SCSC)")]
	SchedaSocio = 9,

	[Display(Name = "Scheda società (SCSO)")]
	SchedaSocieta = 10,

	[Display(Name = "Trasferimenti di azienda (TRSF)")]
	TrasferimentiAzienda = 12,

	[Display(Name = "Visura ordinaria (VISO)")]
	VisuraOrdinaria = 13,

	[Display(Name = "Visura storica (VISS)")]
	VisuraStorica = 14,

	[Display(Name = "Certificato Artigiano (CART)")]
	CertificatoArtigiano = 0,

	[Display(Name = "Certificato Ordinario Sintetico (CRIA)")]
	CertificatoOrdinarioSintetico = 1,

	[Display(Name = "Certificato Ordinario (CRIM)")]
	CertificatoOrdinario = 2,

	[Display(Name = "Certificato Storico (CRIS)")]
	CertificatoStorico = 3,

	[Display(Name = "Dichiarazione Sostitutiva (SOST)")]
	DichiarazioneSostitutiva = 11
}

public enum CurrentState
{
	inAttesa = 0,
	presaInCarico = 1,
	inlavorazione = 2,

	//STATI TEMPORANEI
	accettatoOnline = 9,
	documentoValidato = 11,


	//ERRORI
	ErroreSubmit = 5,
	ErroreValidazione = 6,
	ErroreConfirm = 7,
	ErroreGenerico = 100

}

    public enum WorkStatus
    {
        InCodaInvio = 1,
		InviatoPoste = 2,
        InCodaValorizza = 3,
		InviatoValorizza = 4,
		InCodaConferma = 5,
		InviatoConferma = 6,
        InCodaRecuperaDocumentoFinale = 7,
        InviatoRecuperaDocumentoFinale = 8
    }


    public enum HaveBulletin
    {
        No = 0,
        Si = 1
    }

    public enum ShippingTypes
	{
		Singola = 1,
		Multipla = 2
	}

	public enum PrintType
	{
		BiancoNero = 0,
		Colori = 1
	}

	public enum RR
	{
		Si = 0,
		No = 1
	}

	public enum FrontBack
	{
		SoloFronte = 0,
		FronteRetro = 1
	}

	public enum UserTypes
	{
		Administrator = 1,
		Visualizzatore = 2,
		Inseritore = 3
	}


    public enum FormatType
    {
        A4 = 0,
        FormatoSpeciale
    }

    public enum PdfInsertionPosition
    {
        FirstPage = 1,
        LastPage
    }


    public static class EnumHelper
{
	public static List<(int Value, string Name)> GetEnumValuesWithDisplayName<T>() where T : Enum
	{
		return Enum.GetValues(typeof(T))
			.Cast<T>()
			.Select(e => (
				Convert.ToInt32(e),
				GetDisplayName(e)
			)).ToList();
	}

	private static string GetDisplayName<T>(T enumValue) where T : Enum
	{
		var memberInfo = typeof(T).GetMember(enumValue.ToString()).FirstOrDefault();
		var displayAttr = memberInfo?.GetCustomAttribute<DisplayAttribute>();
		return displayAttr?.Name ?? enumValue.ToString();
	}

	public static string? GetDisplayNameByValue<T>(int value) where T : Enum
	{
		var enumValue = Enum.GetValues(typeof(T))
			.Cast<T>()
			.FirstOrDefault(e => Convert.ToInt32(e) == value);

		var memberInfo = typeof(T).GetMember(enumValue?.ToString() ?? "").FirstOrDefault();
		var displayAttr = memberInfo?.GetCustomAttribute<DisplayAttribute>();
		return displayAttr?.Name;
	}
}
}
