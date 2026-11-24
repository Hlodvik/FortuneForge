# Treasure Cove client package

Treasure Cove's metadata, assets and presentation configuration are packaged here.
The browser game is not yet standalone: its release entry publishes a manifest
that the host renders through `SlotsPage`, `useSlotsPageController` and
`SlotsPageView`. The host's `services/slotsApi.ts` also owns paid request transport
and result validation. An independent package build does not prove game ownership.

This package owns its manifest, catalogue metadata, symbols, cabinet
configuration, styles, audio contract, help content and in-game media. Completing
frontend ownership requires a package-owned game entry, controller, API contract
and presentation lifecycle. Authentication, wallet settlement, Firebase hosting
and the shared Fortune Forge navbar remain host responsibilities. Neutral,
versioned component libraries can remain dependencies of a standalone game.

