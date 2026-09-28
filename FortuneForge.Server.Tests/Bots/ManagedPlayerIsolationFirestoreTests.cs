using FortuneForge.Server.Bots;
using FortuneForge.Server.Tests.Blackjack.Table;
using Google.Cloud.Firestore;
using Xunit;

namespace FortuneForge.Server.Tests.Bots;

public sealed class ManagedPlayerIsolationFirestoreTests(
    BlackjackTableFirestoreEmulatorFixture fixture)
    : IClassFixture<BlackjackTableFirestoreEmulatorFixture>
{
    private static readonly DateTime Now =
        new(2026, 9, 28, 18, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task QueuerAndAssignmentStoreRejectARealUserEvenWhenItHasABotTag()
    {
        var database = Database("real-user-boundary");
        const string userId = "real-user";
        var user = database.Collection("users").Document(userId);
        await user.SetAsync(new Dictionary<string, object>
        {
            ["userId"] = userId,
            ["playerName"] = "RealPlayer",
            ["authProvider"] = "firebase",
            [ManagedPlayerProfileSchema.ProfileTagsField] =
                new[] { ManagedPlayerProfileSchema.BotTag },
            ["supportedGames"] = new[] { ManagedPlayerGames.Blackjack },
            ["createdAt"] = Timestamp.FromDateTime(Now)
        });

        var assignments = new FirestoreManagedPlayerAssignmentStore(database);
        Assert.False(await assignments.TryReserveAsync(
            userId, ManagedPlayerGames.Blackjack, "table-real", Now, default));
        Assert.False((await database.Collection("managedPlayerAssignments")
            .Document(userId).GetSnapshotAsync()).Exists);

        var profiles = new FirestoreManagedPlayerProfileRepository(database);
        await profiles.MarkLastActiveAsync(userId, Now.AddMinutes(1), default);
        Assert.False((await user.GetSnapshotAsync()).ContainsField("lastActiveAt"));

        var generator = new ManagedPlayerProfileGenerator(profiles);
        var queuer = new ManagedPlayerQueuer(generator, profiles, assignments);
        var selected = Assert.Single(await queuer.ReserveAsync(
            ManagedPlayerGames.Blackjack, "table-generated", 1, [], Now, default));

        Assert.NotEqual(userId, selected.UserId);
        var generated = await database.Collection("users")
            .Document(selected.UserId).GetSnapshotAsync();
        Assert.True(FirestoreManagedPlayerProfileRepository.IsManagedPlayer(generated));
    }

    [Fact]
    public async Task QueuerDoesNotReuseLegacyManagedProfilesWithoutTheBotTag()
    {
        var database = Database("legacy-profile-boundary");
        const string legacyId = "managed-legacy-static-profile";
        await database.Collection("users").Document(legacyId).SetAsync(
            new Dictionary<string, object>
            {
                ["userId"] = legacyId,
                ["playerName"] = "LegacyStaticProfile",
                ["authProvider"] = ManagedPlayerProfileSchema.AuthenticationProvider,
                ["supportedGames"] = new[] { ManagedPlayerGames.Blackjack },
                ["skillLevel"] = 3L,
                ["createdAt"] = Timestamp.FromDateTime(Now)
            });

        var profiles = new FirestoreManagedPlayerProfileRepository(database);
        var assignments = new FirestoreManagedPlayerAssignmentStore(database);
        var queuer = new ManagedPlayerQueuer(
            new ManagedPlayerProfileGenerator(profiles), profiles, assignments);
        var selected = Assert.Single(await queuer.ReserveAsync(
            ManagedPlayerGames.Blackjack, "table-new-profile", 1, [], Now, default));

        Assert.NotEqual(legacyId, selected.UserId);
        Assert.False((await database.Collection("managedPlayerAssignments")
            .Document(legacyId).GetSnapshotAsync()).Exists);
    }

    private FirestoreDb Database(string purpose) => new FirestoreDbBuilder
    {
        ProjectId = $"demo-fortuneforge-{purpose}-{Guid.NewGuid():N}",
        Endpoint = fixture.Endpoint,
        ChannelCredentials = Grpc.Core.ChannelCredentials.Insecure,
        EmulatorDetection = Google.Api.Gax.EmulatorDetection.None
    }.Build();
}
