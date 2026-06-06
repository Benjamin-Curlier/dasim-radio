using Dasim.Radio.Contracts;
using Dasim.Radio.Messaging.KeyValue;

namespace Dasim.Radio.MediaService.Floor;

/// <summary>Writes floor state to the <c>floor_state</c> KV bucket via the control-plane store.</summary>
public sealed class ControlPlaneFloorStateWriter(IControlPlaneStore store) : IFloorStateWriter
{
    private readonly IControlPlaneStore _store = store ?? throw new ArgumentNullException(nameof(store));

    public async ValueTask WriteAsync(FloorStateDto state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        // The store binds and caches the bucket, so resolving it per call is cheap.
        INatsKeyValueStore<FloorStateDto> bucket = await _store.FloorStateAsync(cancellationToken)
            .ConfigureAwait(false);
        await bucket.PutAsync(state.NetId, state, cancellationToken).ConfigureAwait(false);
    }
}
