using Google.Cloud.Firestore;

namespace FortuneForge.Server.Bots;

public static class ManagedPlayerConfiguration
{
    public static IServiceCollection AddManagedPlayers(this IServiceCollection services)
    {
        services.AddSingleton<IManagedPlayerProfileRepository>(provider =>
            new FirestoreManagedPlayerProfileRepository(
                provider.GetRequiredService<FirestoreDb>()));
        services.AddSingleton<IManagedPlayerAssignmentStore>(provider =>
            new FirestoreManagedPlayerAssignmentStore(
                provider.GetRequiredService<FirestoreDb>(),
                provider.GetRequiredService<IConfiguration>().GetValue("ManagedPlayers:ProvisionalReservationBatchesEnabled", false)));
        services.AddSingleton<IManagedPlayerProfileGenerator, ManagedPlayerProfileGenerator>();
        services.AddSingleton<IManagedPlayerQueuer, ManagedPlayerQueuer>();
        services.AddSingleton<IManagedTablePopulationDirector, ManagedTablePopulationDirector>();
        return services;
    }
}
