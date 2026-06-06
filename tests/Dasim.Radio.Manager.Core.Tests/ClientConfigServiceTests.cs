using Dasim.Radio.Contracts;
using Dasim.Radio.Manager.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Dasim.Radio.Manager.Core.Tests;

public sealed class ClientConfigServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ClientConfigDto Config(string id = "cfg1") =>
        new(id, "c1", "p1", "alpha", null, "Alpha One");

    private static (ClientConfigService Service, FakeKeyValueStore<ClientConfigDto> Bucket) Build()
    {
        var bucket = new FakeKeyValueStore<ClientConfigDto>(Subjects.Buckets.Configs);
        var service = new ClientConfigService(
            new FakeControlPlaneStore(configs: bucket), NullLogger<ClientConfigService>.Instance);
        return (service, bucket);
    }

    [Fact]
    public async Task Create_then_get_round_trips()
    {
        (ClientConfigService service, _) = Build();

        await service.CreateAsync(Config(), Ct);
        ClientConfigEntry? entry = await service.GetAsync("cfg1", Ct);

        Assert.NotNull(entry);
        Assert.Equal("c1", entry!.Config.ClientId);
        Assert.Equal(1ul, entry.Revision);
    }

    [Fact]
    public async Task Create_rejects_a_duplicate()
    {
        (ClientConfigService service, _) = Build();
        await service.CreateAsync(Config(), Ct);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(Config(), Ct).AsTask());
    }

    [Theory]
    [InlineData("cfg.1")]
    [InlineData("cfg 1")]
    [InlineData("cfg*")]
    public async Task Create_rejects_a_non_token_config_id(string badId)
    {
        (ClientConfigService service, FakeKeyValueStore<ClientConfigDto> bucket) = Build();

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(Config(badId), Ct).AsTask());
        Assert.Null(await service.GetAsync("anything", Ct)); // nothing written
        Assert.Empty(bucket.Deleted);
    }

    [Fact]
    public async Task Update_honours_optimistic_concurrency()
    {
        (ClientConfigService service, _) = Build();
        await service.CreateAsync(Config(), Ct);
        ClientConfigEntry entry = (await service.GetAsync("cfg1", Ct))!;

        await service.UpdateAsync(entry.Config with { DisplayName = "Renamed" }, entry.Revision, Ct);

        ClientConfigEntry updated = (await service.GetAsync("cfg1", Ct))!;
        Assert.Equal("Renamed", updated.Config.DisplayName);

        // The original (stale) revision must now be rejected.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.UpdateAsync(entry.Config, entry.Revision, Ct).AsTask());
    }

    [Fact]
    public async Task Get_returns_null_for_an_absent_config()
    {
        (ClientConfigService service, _) = Build();

        Assert.Null(await service.GetAsync("missing", Ct));
    }

    [Fact]
    public async Task List_returns_all_configs()
    {
        (ClientConfigService service, _) = Build();
        await service.CreateAsync(Config("cfg1"), Ct);
        await service.CreateAsync(Config("cfg2"), Ct);

        IReadOnlyList<ClientConfigEntry> all = await service.ListAsync(Ct);

        Assert.Equal(2, all.Count);
    }

    [Fact]
    public async Task Delete_removes_the_config()
    {
        (ClientConfigService service, FakeKeyValueStore<ClientConfigDto> bucket) = Build();
        await service.CreateAsync(Config(), Ct);

        await service.DeleteAsync("cfg1", Ct);

        Assert.Null(await service.GetAsync("cfg1", Ct));
        Assert.Contains("cfg1", bucket.Deleted);
    }

    [Fact]
    public async Task List_skips_a_key_that_vanished_after_enumeration()
    {
        (ClientConfigService service, FakeKeyValueStore<ClientConfigDto> bucket) = Build();
        await service.CreateAsync(Config("cfg1"), Ct);
        bucket.GhostKeys.Add("cfg-gone"); // listed but no value (deleted between GetKeys and TryGet)

        IReadOnlyList<ClientConfigEntry> all = await service.ListAsync(Ct);

        ClientConfigEntry only = Assert.Single(all);
        Assert.Equal("cfg1", only.Config.ConfigId);
    }

    [Theory]
    [InlineData("clientId")]
    [InlineData("participantId")]
    [InlineData("ownNetId")]
    [InlineData("parentNetId")]
    [InlineData("hostId")]
    public async Task Create_rejects_a_non_token_in_any_id_field(string field)
    {
        (ClientConfigService service, _) = Build();
        const string bad = "x>y";
        ClientConfigDto config = field switch
        {
            "clientId" => Config() with { ClientId = bad },
            "participantId" => Config() with { ParticipantId = bad },
            "ownNetId" => Config() with { OwnNetId = bad },
            "parentNetId" => Config() with { ParentNetId = bad },
            _ => Config() with { HostId = bad },
        };

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(config, Ct).AsTask());
    }

    [Fact]
    public async Task Create_rejects_an_empty_display_name()
    {
        (ClientConfigService service, _) = Build();

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.CreateAsync(Config() with { DisplayName = "  " }, Ct).AsTask());
    }
}
