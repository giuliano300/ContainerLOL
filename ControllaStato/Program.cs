using ControllaStato.Services;
using Microsoft.EntityFrameworkCore;
using Serilog;
using SharedLib.Config;
using SharedLib.Db;
using SharedLib.Services;

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
builder.Services.AddSingleton<IControllaStatoQueue, ControllaStatoQueue>();
builder.Services.AddSingleton<IControllaStatoQueueTracker, ControllaStatoQueueTracker>();
builder.Services.AddHostedService<ControllaStatoProcessor>();
builder.Services.AddHostedService<ControllaStatoWatcher>();
builder.Services.AddScoped<ILogService, LogService>();

builder.Services.AddControllers();

var app = builder.Build();

app.Run();
