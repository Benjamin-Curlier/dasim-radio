using Dasim.Radio.Manager.Components;
using Dasim.Radio.Manager.Core;
using Dasim.Radio.Messaging;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents().AddInteractiveServerComponents();

string natsUrl = builder.Configuration["Nats:Url"] ?? "nats://srv_brk:4222";
builder.Services.AddDasimRadioMessaging(natsUrl);
builder.Services.AddDasimRadioManagerCore(builder.Configuration);

WebApplication app = builder.Build();

app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Run();
