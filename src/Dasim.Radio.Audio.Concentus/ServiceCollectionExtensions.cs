using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Dasim.Radio.Audio.Concentus;

/// <summary>DI registration for the Concentus (managed Opus) codec.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IOpusEncoderFactory"/> and <see cref="IOpusDecoderFactory"/> backed by
    /// Concentus. The factories are stateless and thread-safe, so they are singletons; each
    /// encoder/decoder they create is single-stream and must not be shared across threads.
    /// </summary>
    public static IServiceCollection AddConcentusOpusCodec(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IOpusEncoderFactory, ConcentusOpusEncoderFactory>();
        services.TryAddSingleton<IOpusDecoderFactory, ConcentusOpusDecoderFactory>();

        return services;
    }
}
