# Jade Dragon

Jade Dragon is a complete standalone slot module. The former Empty Slots scaffold
has been absorbed into this game and no longer exists as a separate route, catalogue
entry, package, or server identity.

The browser presentation lives in `client/jade-dragon` and is built independently
before the Fortune Forge host. The authenticated route is `/slots/jade-dragon`; it
keeps the shared navbar and embeds the presentation through a narrow same-origin
message bridge. The existing Jade Dragon catalogue card remains host-owned lobby art.

New rounds use `jade-dragon-cascade-v2`. The package owns the weighted draws,
1,024 ways, cascade refills, multiplier ladders, and free-spin rules. Each
cascade returns the cleared positions, next grid, fall distances, and credited
amount. The browser animates that result and the host settles the round once.

The wager denomination is 25 cents per point. In v2, the explicit
`payoutPointValueInCents` field is 1 so even the smallest matching board can
credit a cent. `empty-slots-engine-v1` and its `S01`–`S23` symbols remain
available for existing free spins and historical replay. `SYMBOLS.md`
documents those legacy roles. Authentication and wallet settlement belong
to the existing application.

The .NET 10 backend project is
`games/JadeDragon/src/FortuneForge.Games.JadeDragon/FortuneForge.Games.JadeDragon.csproj`.
Its host adapter is `FortuneForge.Server/Slots/Games/JadeDragon`.

Canonical route and game identifiers are `jade-dragon` and `empty-slots-v1`.
The legacy `/slots/empty-slots-game` URL redirects to
the canonical route; it is not another game.

Run the active engine's seeded RTP check from the existing checkout:

```powershell
dotnet run --project tools/FortuneForge.SlotMath --configuration Release -- 1000000 --game empty-slots-v1 --seed=20261111
```
