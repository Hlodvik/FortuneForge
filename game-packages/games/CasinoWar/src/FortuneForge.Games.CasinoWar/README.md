# Casino War

This package models the selected Casino War variant with Ace high. An opening tie lets the player Surrender or Go to War. Going to War burns three cards before the Player's War card, then burns three more before the Dealer's War card. A second tie favors the Player and returns four units on two units wagered.

The optional Tie side bet wins on an opening tie and pays 10:1 profit. `CasinoWarOpeningDealer` and `CasinoWarRoundEngine` consume explicit ordered shoes, providing a deterministic seam for hosts and tests. Hosts own secure shoe generation and shuffling.
