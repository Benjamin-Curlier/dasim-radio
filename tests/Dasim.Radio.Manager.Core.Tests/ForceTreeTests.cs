using Dasim.Radio.Contracts;
using Dasim.Radio.Manager.Core;
using Dasim.Radio.Messaging.KeyValue;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Dasim.Radio.Manager.Core.Tests;

public sealed class ForceTreeValidatorTests
{
    private static ForceNodeDto Node(string id, string name, params ForceNodeDto[] children) =>
        new(id, name, "echelon", 5, children);

    private static ForceTreeDto Valid() =>
        new(1, Node("root", "HQ", Node("alpha", "Alpha"), Node("bravo", "Bravo")));

    [Fact]
    public void A_well_formed_tree_has_no_errors()
    {
        Assert.Empty(ForceTreeValidator.Validate(Valid()));
    }

    [Fact]
    public void A_duplicate_id_is_an_error()
    {
        ForceTreeDto tree = new(1, Node("root", "HQ", Node("alpha", "Alpha"), Node("alpha", "Dup")));

        Assert.Contains(ForceTreeValidator.Validate(tree), e => e.Contains("Duplicate"));
    }

    [Fact]
    public void A_non_token_id_is_an_error()
    {
        ForceTreeDto tree = new(1, Node("root", "HQ", Node("al.pha", "Alpha")));

        Assert.Contains(ForceTreeValidator.Validate(tree), e => e.Contains("al.pha"));
    }

    [Fact]
    public void An_empty_name_is_an_error()
    {
        ForceTreeDto tree = new(1, Node("root", "HQ", Node("alpha", "  ")));

        Assert.Contains(ForceTreeValidator.Validate(tree), e => e.Contains("empty name"));
    }

    [Fact]
    public void A_leaf_with_null_children_validates_without_throwing()
    {
        // JSON omitting "children" deserializes to a null array despite the non-nullable type.
        ForceTreeDto tree = new(1, new ForceNodeDto("root", "HQ", "echelon", 9,
            [new ForceNodeDto("leaf", "Leaf", "echelon", 5, null!)]));

        Assert.Empty(ForceTreeValidator.Validate(tree));
    }

    [Fact]
    public void Normalize_replaces_null_children_with_an_empty_array()
    {
        ForceTreeDto tree = new(1, new ForceNodeDto("root", "HQ", "echelon", 9, null!));

        ForceTreeDto normalized = ForceTreeValidator.Normalize(tree);

        Assert.NotNull(normalized.Root.Children);
        Assert.Empty(normalized.Root.Children);
    }
}

public sealed class ForceTreeServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ForceTreeDto Valid() =>
        new(1, new ForceNodeDto("root", "HQ", "echelon", 9, [new ForceNodeDto("alpha", "Alpha", "echelon", 5, [])]));

    private static (ForceTreeService Service, FakeKeyValueStore<ForceTreeDto> Bucket) Build()
    {
        var bucket = new FakeKeyValueStore<ForceTreeDto>(Subjects.Buckets.ForceTree);
        var service = new ForceTreeService(
            new FakeControlPlaneStore(forceTree: bucket), NullLogger<ForceTreeService>.Instance);
        return (service, bucket);
    }

    [Fact]
    public async Task Get_current_is_null_when_unset()
    {
        (ForceTreeService service, _) = Build();

        Assert.Null(await service.GetCurrentAsync(Ct));
    }

    [Fact]
    public async Task Import_stores_a_valid_tree_under_the_current_key()
    {
        (ForceTreeService service, FakeKeyValueStore<ForceTreeDto> bucket) = Build();

        await service.ImportAsync(Valid(), cancellationToken: Ct);

        KeyValueEntry<ForceTreeDto>? stored = await bucket.TryGetAsync(Subjects.Keys.ForceTreeCurrent, Ct);
        Assert.NotNull(stored);
        ForceTreeImport? current = await service.GetCurrentAsync(Ct);
        Assert.Equal(1, current!.Tree.Version);
    }

    [Fact]
    public async Task Import_with_a_stale_revision_is_rejected_so_a_concurrent_import_is_not_lost()
    {
        (ForceTreeService service, _) = Build();
        await service.ImportAsync(Valid(), cancellationToken: Ct); // first import (create)
        ulong rev = (await service.GetCurrentAsync(Ct))!.Revision;

        // A concurrent admin imports against that same revision, advancing the stored revision.
        await service.ImportAsync(Valid() with { Version = 2 }, expectedRevision: rev, cancellationToken: Ct);

        // Our import still using the now-stale revision must be rejected, not silently overwrite theirs.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ImportAsync(Valid() with { Version = 3 }, expectedRevision: rev, cancellationToken: Ct).AsTask());

        Assert.Equal(2, (await service.GetCurrentAsync(Ct))!.Tree.Version); // the concurrent import survived
    }

    [Fact]
    public async Task Import_with_the_current_revision_succeeds()
    {
        (ForceTreeService service, _) = Build();
        await service.ImportAsync(Valid(), cancellationToken: Ct);
        ulong rev = (await service.GetCurrentAsync(Ct))!.Revision;

        await service.ImportAsync(Valid() with { Version = 2 }, expectedRevision: rev, cancellationToken: Ct);

        Assert.Equal(2, (await service.GetCurrentAsync(Ct))!.Tree.Version);
    }

    [Fact]
    public async Task First_import_when_a_tree_already_exists_is_rejected()
    {
        (ForceTreeService service, _) = Build();
        await service.ImportAsync(Valid(), cancellationToken: Ct);

        // A revision-less import treats the write as a first create; it must not blind-overwrite an existing tree.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ImportAsync(Valid() with { Version = 9 }, cancellationToken: Ct).AsTask());
    }

    [Fact]
    public async Task Import_rejects_an_invalid_tree_and_does_not_store_it()
    {
        (ForceTreeService service, FakeKeyValueStore<ForceTreeDto> bucket) = Build();
        ForceTreeDto invalid = new(1, new ForceNodeDto("root", "HQ", "echelon", 9,
            [new ForceNodeDto("dup", "A", "echelon", 5, []), new ForceNodeDto("dup", "B", "echelon", 5, [])]));

        await Assert.ThrowsAsync<ForceTreeValidationException>(() => service.ImportAsync(invalid, cancellationToken: Ct).AsTask());
        Assert.Null(await bucket.TryGetAsync(Subjects.Keys.ForceTreeCurrent, Ct));
    }

    [Fact]
    public async Task Import_normalizes_null_children_so_downstream_never_sees_null()
    {
        (ForceTreeService service, _) = Build();
        ForceTreeDto tree = new(1, new ForceNodeDto("root", "HQ", "echelon", 9,
            [new ForceNodeDto("alpha", "Alpha", "echelon", 5, null!)]));

        await service.ImportAsync(tree, cancellationToken: Ct);

        ForceTreeImport current = (await service.GetCurrentAsync(Ct))!;
        Assert.NotNull(current.Tree.Root.Children[0].Children);
        Assert.Empty(current.Tree.Root.Children[0].Children);
    }
}
