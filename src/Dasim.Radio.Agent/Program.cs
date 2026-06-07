using Dasim.Radio.Agent;
using Dasim.Radio.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

RadioNatsOptions natsOptions =
    builder.Configuration.GetSection(RadioNatsOptions.SectionName).Get<RadioNatsOptions>() ?? new RadioNatsOptions();

builder.Services.AddDasimRadioMessaging(natsOptions);
builder.Services.AddDasimRadioAgent(builder.Configuration);

// Integrate with the OS service manager. Both are no-ops when not run as a service, so the same
// binary works as a console app on either platform.
builder.Services.AddSystemd();
builder.Services.AddWindowsService();

IHost host = builder.Build();
host.Run();
