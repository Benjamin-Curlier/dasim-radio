using Dasim.Radio.MediaService.Floor;
using Dasim.Radio.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

string natsUrl = builder.Configuration.GetValue<string>("Nats:Url") ?? "nats://srv_brk:4222";

builder.Services.AddDasimRadioMessaging(natsUrl);
builder.Services.AddFloorAuthority();

IHost host = builder.Build();
host.Run();
