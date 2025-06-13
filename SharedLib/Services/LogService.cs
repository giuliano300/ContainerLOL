using Microsoft.Extensions.Logging;

namespace SharedLib.Services
{
    public class LogService : ILogService
    {
        private readonly ILogger<LogService> _logger;

        public LogService(ILogger<LogService> logger)
        {
            _logger = logger;
        }

        public void Info(string message)
        {
            _logger.LogInformation(message);
        }

        public void Error(Exception ex, string context)
        {
            _logger.LogError(ex, "Errore in {Context}: {Message}", context, ex.Message);
        }
    }
}
