using Dasim.Radio.MediaService.Floor;
using Dasim.Radio.MediaService.Routing;
using Dasim.Radio.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

RadioNatsOptions natsOptions =
    builder.Configuration.GetSection(RadioNatsOptions.SectionName).Get<RadioNatsOptions>() ?? new RadioNatsOptions();

MixCombinePolicy combinePolicy =
    Enum.TryParse(builder.Configuration["Routing:CombinePolicy"], ignoreCase: true, out MixCombinePolicy parsed)
        ? parsed
        : MixCombinePolicy.Override;

builder.Services.AddDasimRadioMessaging(natsOptions);
builder.Services.AddFloorAuthority();
builder.Services.AddMediaRouting(combinePolicy);

IHost host = builder.Build();
host.Run();
