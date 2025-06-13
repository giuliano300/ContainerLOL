using ServiceReference;
using SharedLib.Models;
using SharedLib.WsdlModels;

namespace SharedLib.Services;

public interface IServiceSoapClient
{
    Task InvioAsync(InvioItem item);
    Task RecuperaStatoAsync(object richiesta);     // definire modello reale
    Task RecuperaServizioAsync(object richiesta);  // definire modello reale
}
