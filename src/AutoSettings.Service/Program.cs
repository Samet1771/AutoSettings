using AutoSettings.Core;
using AutoSettings.Core.Engine;
using AutoSettings.Service;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Logging;
using Serilog;

// AutoSettings Windows Service.
// Run from a console for development (it then cannot start agents or see session changes),
// or install it with scripts/install-dev.ps1 / the MSI.

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

builder.Services.AddWindowsService(options => options.ServiceName = Product.ServiceName);
if (WindowsServiceHelpers.IsWindowsService())
    builder.Services.AddSingleton<IHostLifetime, SessionAwareServiceLifetime>();

builder.Services.AddSerilog((_, configuration) => configuration
    .MinimumLevel.Information()
    .Enrich.FromLogContext()
    .WriteTo.File(
        Path.Combine(Product.ServiceLogDirectory, "service-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
    .WriteTo.Console());

builder.Services.Configure<ServiceOptions>(builder.Configuration.GetSection("AutoSettings"));
builder.Services.AddSingleton<SystemSignals>();
builder.Services.AddSingleton(provider =>
    new ActivityLog(1000, provider.GetRequiredService<ILoggerFactory>().CreateLogger("Activity")));
builder.Services.AddSingleton<AgentHub>();
builder.Services.AddSingleton<AgentSupervisor>();
builder.Services.AddSingleton<MachineHost>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<MachineHost>());
builder.Services.AddHostedService<PipeServer>();

var host = builder.Build();
await host.RunAsync();
