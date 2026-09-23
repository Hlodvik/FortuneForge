# Baccarat

This package models standard Punto Banco: card values, natural detection, the Player/Banker third-card tableau, deterministic ordered-shoe dealing, and one-bet settlement.

Banker wins return the stake plus profit after the standard 5% commission; Tie wins pay 8:1 profit. `PuntoBancoRoundDealer` consumes an explicit ordered shoe for deterministic host and test behavior. Hosts own secure shoe generation and shuffling.
