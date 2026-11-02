# Mines integration

The original batch integration plan has been implemented and superseded by
[HOST_INTEGRATION.md](HOST_INTEGRATION.md).

Use that document for current account-credit routing, atomic wallet/history behavior,
server-only Firestore rules, verification and release gates. The public website has no practice
mode. New wagers use R1–R5, payout policy 2, a 50× gross-return ceiling and full-board play;
see [RULES.md](RULES.md) for the exact 90% nominal-RTP schedule and accepted-round compatibility.

Source integration is not a live deployment. The paid flag remains off pending the normal
reviewed API/client/rules release. No earlier practice-mode planning example should be used to
wire the production route.
