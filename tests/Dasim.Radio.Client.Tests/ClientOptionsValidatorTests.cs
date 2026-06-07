using Dasim.Radio.Audio;
using Dasim.Radio.Client;
using Microsoft.Extensions.Options;
using Xunit;

namespace Dasim.Radio.Client.Tests;

public sealed class ClientOptionsValidatorTests
{
    private static ClientOptions Valid() => new()
    {
        ClientId = "c1",
        ParticipantId = "p1",
        OwnNetId = "alpha",
        ParentNetId = "root",
    };

    [Fact]
    public void Accepts_valid_options()
    {
        Assert.True(new ClientOptionsValidator().Validate(null, Valid()).Succeeded);
    }

    [Fact]
    public void Accepts_a_null_parent_net()
    {
        ClientOptions options = Valid();
        options.ParentNetId = null;

        Assert.True(new ClientOptionsValidator().Validate(null, options).Succeeded);
    }

    [Theory]
    [InlineData("")]
    [InlineData("c.1")]
    [InlineData("c 1")]
    public void Rejects_a_bad_client_id(string clientId)
    {
        ClientOptions options = Valid();
        options.ClientId = clientId;

        Assert.True(new ClientOptionsValidator().Validate(null, options).Failed);
    }

    [Fact]
    public void Rejects_a_bad_participant_id()
    {
        ClientOptions options = Valid();
        options.ParticipantId = "p*";

        Assert.True(new ClientOptionsValidator().Validate(null, options).Failed);
    }

    [Fact]
    public void Rejects_a_bad_own_net()
    {
        ClientOptions options = Valid();
        options.OwnNetId = "a>b";

        Assert.True(new ClientOptionsValidator().Validate(null, options).Failed);
    }

    [Fact]
    public void Rejects_a_bad_parent_net()
    {
        ClientOptions options = Valid();
        options.ParentNetId = "root.1";

        Assert.True(new ClientOptionsValidator().Validate(null, options).Failed);
    }

    [Theory]
    [InlineData(0)]     // bitrate must be positive
    [InlineData(-100)]
    public void Rejects_invalid_encoder_bitrate(int bitrate)
    {
        ClientOptions options = Valid();
        options.EncoderSettings = new OpusEncoderSettings { BitrateBitsPerSecond = bitrate };

        // Without this, a bad encoder value only surfaces inside the fire-and-forget transmit pump,
        // silently killing transmission; the validator must reject it at startup instead.
        Assert.True(new ClientOptionsValidator().Validate(null, options).Failed);
    }

    [Fact]
    public void Rejects_an_out_of_range_encoder_complexity()
    {
        ClientOptions options = Valid();
        options.EncoderSettings = new OpusEncoderSettings { Complexity = 99 };

        Assert.True(new ClientOptionsValidator().Validate(null, options).Failed);
    }

    [Fact]
    public void Accepts_valid_encoder_settings()
    {
        ClientOptions options = Valid();
        options.EncoderSettings = new OpusEncoderSettings { BitrateBitsPerSecond = 16_000, Complexity = 5 };

        Assert.True(new ClientOptionsValidator().Validate(null, options).Succeeded);
    }
}
