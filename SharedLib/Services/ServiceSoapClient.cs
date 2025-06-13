using Microsoft.Extensions.Options;
using SharedLib.Config;
using SharedLib.WsdlModels;

namespace SharedLib.Services;

public class ServiceSoapClient : IServiceSoapClient
{
    private readonly LolServiceOptions _options;

    public ServiceSoapClient(IOptions<LolServiceOptions> options)
    {
        _options = options.Value;
    }

    public Task InvioAsync(InvioItem item)
    {
        return Task.CompletedTask;
    }

    public Task RecuperaStatoAsync(object richiesta)
    {
        return Task.CompletedTask;
    }

    public Task RecuperaServizioAsync(object richiesta)
    {
        return Task.CompletedTask;
    }
}
