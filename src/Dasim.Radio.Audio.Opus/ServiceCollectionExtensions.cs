using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Dasim.Radio.Audio.Opus;

/// <summary>DI registration for the native-libopus (OpusSharp) codec used by the media service.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IOpusEncoderFactory"/> and <see cref="IOpusDecoderFactory"/> backed by
    /// native libopus (OpusSharp + OpusSharp.Natives). The factories are stateless and thread-safe,
    /// so they are singletons; each encoder/decoder they create is single-stream and must not be
    /// shared across threads.
    /// </summary>
    public static IServiceCollection AddOpusSharpCodec(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IOpusEncoderFactory, OpusSharpEncoderFactory>();
        services.TryAddSingleton<IOpusDecoderFactory, OpusSharpDecoderFactory>();

        return services;
    }
}
