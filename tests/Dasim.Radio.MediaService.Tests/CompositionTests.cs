using Dasim.Radio.MediaService.Floor;
using Dasim.Radio.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Dasim.Radio.MediaService.Tests;

public sealed class CompositionTests
{
    [Fact]
    public async Task Composition_root_resolves_the_floor_authority()
    {
        // Mirrors Program.cs. ValidateOnBuild eagerly constructs every registration, so a missing or
        // mis-lifetimed dependency fails here rather than at host startup. No NATS server is needed —
        // the connection is created lazily and not contacted during resolution. The provider is
        // disposed asynchronously because NatsConnection is IAsyncDisposable-only.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDasimRadioMessaging("nats://localhost:4222");
        services.AddFloorAuthority();

        await using ServiceProvider provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        Assert.NotNull(provider.GetRequiredService<FloorArbiter>());
        Assert.Contains(provider.GetServices<IHostedService>(), service => service is FloorAuthorityService);
    }
}
