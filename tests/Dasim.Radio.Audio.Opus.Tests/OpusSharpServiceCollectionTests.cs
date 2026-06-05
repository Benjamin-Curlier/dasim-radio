using Dasim.Radio.Audio;
using Dasim.Radio.Audio.Opus;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Dasim.Radio.Audio.Opus.Tests;

public sealed class OpusSharpServiceCollectionTests
{
    [Fact]
    public void AddOpusSharpCodec_registers_usable_factories()
    {
        using ServiceProvider provider = new ServiceCollection()
            .AddOpusSharpCodec()
            .BuildServiceProvider();

        var encoderFactory = provider.GetRequiredService<IOpusEncoderFactory>();
        var decoderFactory = provider.GetRequiredService<IOpusDecoderFactory>();

        Assert.IsType<OpusSharpEncoderFactory>(encoderFactory);
        Assert.IsType<OpusSharpDecoderFactory>(decoderFactory);

        using IOpusEncoder encoder = encoderFactory.Create(AudioFormat.Voice);
        using IOpusDecoder decoder = decoderFactory.Create(AudioFormat.Voice);

        Assert.Equal(AudioFormat.Voice, encoder.Format);
        Assert.Equal(AudioFormat.Voice, decoder.Format);
    }

    [Fact]
    public void AddOpusSharpCodec_is_idempotent()
    {
        var services = new ServiceCollection();

        services.AddOpusSharpCodec().AddOpusSharpCodec();

        Assert.Single(services, d => d.ServiceType == typeof(IOpusEncoderFactory));
        Assert.Single(services, d => d.ServiceType == typeof(IOpusDecoderFactory));
    }
}
