# Video Poker

This package currently provides a full-pay 9/6 Jacks-or-Better hand evaluator and paytable, plus a deterministic single-round deal/hold/draw engine.

`VideoPokerRoundEngine` accepts an ordered standard deck so hosts and tests can reproduce a round exactly. That ordered-deck seam is not a production random-number generator.
