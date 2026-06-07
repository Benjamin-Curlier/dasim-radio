using Dasim.Radio.Manager.Components;
using Dasim.Radio.Manager.Core;
using Dasim.Radio.Messaging;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents().AddInteractiveServerComponents();

RadioNatsOptions natsOptions =
    builder.Configuration.GetSection(RadioNatsOptions.SectionName).Get<RadioNatsOptions>() ?? new RadioNatsOptions();
builder.Services.AddDasimRadioMessaging(natsOptions);
builder.Services.AddDasimRadioManagerCore(builder.Configuration);

WebApplication app = builder.Build();

app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Run();
