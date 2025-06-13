using SharedLib.Db;
using SharedLib.Services;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Valorizza.Services;
using SharedLib.Config;
using SharedLib.Models;

var builder = WebApplication.CreateBuilder(args);

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.File("Logs/log-.txt", rollingInterval: RollingInterval.Day)
    .CreateLogger();

builder.Host.UseSerilog();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.Configure<LolServiceOptions>(
    builder.Configuration.GetSection("LOLService"));


builder.Services.AddSingleton<IServiceSoapClient, ServiceSoapClient>();
builder.Services.AddSingleton<IValorizzaQueue, ValorizzaQueue>();
builder.Services.AddSingleton<IValorizzaQueueTracker, ValorizzaQueueTracker>();
builder.Services.AddHostedService<ValorizzaProcessor>();
builder.Services.AddHostedService<ValorizzaWatcher>();
builder.Services.AddScoped<ILogService, LogService>();


builder.Services.AddControllers();

var app = builder.Build();

app.MapControllers();
app.Run();
