using Dasim.Radio.Audio;
using Dasim.Radio.Audio.Concentus;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Dasim.Radio.Audio.Tests;

public sealed class ConcentusServiceCollectionTests
{
    [Fact]
    public void AddConcentusOpusCodec_registers_usable_factories()
    {
        using ServiceProvider provider = new ServiceCollection()
            .AddConcentusOpusCodec()
            .BuildServiceProvider();

        var encoderFactory = provider.GetRequiredService<IOpusEncoderFactory>();
        var decoderFactory = provider.GetRequiredService<IOpusDecoderFactory>();

        Assert.IsType<ConcentusOpusEncoderFactory>(encoderFactory);
        Assert.IsType<ConcentusOpusDecoderFactory>(decoderFactory);

        using IOpusEncoder encoder = encoderFactory.Create(AudioFormat.Voice);
        using IOpusDecoder decoder = decoderFactory.Create(AudioFormat.Voice);

        Assert.Equal(AudioFormat.Voice, encoder.Format);
        Assert.Equal(AudioFormat.Voice, decoder.Format);
    }

    [Fact]
    public void AddConcentusOpusCodec_is_idempotent()
    {
        var services = new ServiceCollection();

        services.AddConcentusOpusCodec().AddConcentusOpusCodec();

        Assert.Single(services, d => d.ServiceType == typeof(IOpusEncoderFactory));
        Assert.Single(services, d => d.ServiceType == typeof(IOpusDecoderFactory));
    }
}
