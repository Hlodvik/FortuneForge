using FortuneForge.Server.Slots.Configuration;
using FortuneForge.Server.Slots.Models;

namespace FortuneForge.Server.Slots.Reels;

public static class MidnightVaultBaseReelSet
{
    public const string Id = "midnight-vault-reels-v1";
    public const string CurrentId = "midnight-vault-reels-v2";

    // Frozen awarded v1/v2 strips, independent of future shared prototype changes.
    private static readonly string[][] HistoricalReels =
    [
        [
            "6", "4", "4", "3", "7", "ACE", "4", "5",
            "FREE", "4", "2", "6", "5", "2", "3", "3",
            "4", "3", "POWER", "4", "2", "3", "4", "6",
            "4", "3", "5", "3", "3", "2", "3", "POWER",
            "FREE", "6", "4", "2", "6", "2", "2", "5",
            "3", "5", "3", "2", "5", "5", "7", "2",
            "7", "3", "3", "4", "POWER", "FREE", "POWER", "5",
            "8", "2", "POWER", "3", "6", "2", "5", "3"
        ],
        [
            "5", "7", "5", "4", "2", "3", "3", "6",
            "POWER", "FREE", "3", "2", "3", "2", "2", "3",
            "4", "6", "2", "3", "4", "3", "3", "ACE",
            "POWER", "5", "4", "7", "6", "3", "3", "3",
            "6", "FREE", "6", "5", "3", "4", "5", "4",
            "7", "5", "2", "POWER", "POWER", "4", "4", "5",
            "2", "5", "2", "4", "2", "POWER", "4", "8",
            "6", "5", "FREE", "3", "2", "2", "3", "3"
        ],
        [
            "4", "7", "3", "2", "3", "5", "FREE", "5",
            "4", "4", "5", "2", "3", "2", "4", "2",
            "6", "2", "ACE", "POWER", "2", "5", "7", "3",
            "POWER", "3", "2", "4", "3", "3", "FREE", "5",
            "6", "6", "2", "3", "6", "3", "2", "5",
            "POWER", "5", "6", "3", "3", "2", "4", "7",
            "POWER", "5", "4", "3", "6", "4", "3", "4",
            "8", "FREE", "2", "4", "3", "5", "POWER", "3"
        ],
        [
            "7", "5", "4", "2", "2", "3", "3", "4",
            "4", "6", "4", "5", "6", "4", "FREE", "4",
            "6", "2", "5", "ACE", "POWER", "7", "2", "POWER",
            "6", "6", "2", "3", "2", "7", "3", "3",
            "5", "6", "3", "FREE", "POWER", "5", "3", "3",
            "POWER", "2", "4", "5", "3", "3", "5", "2",
            "5", "2", "3", "2", "3", "5", "4", "FREE",
            "POWER", "2", "8", "3", "4", "3", "4", "3"
        ],
        [
            "2", "5", "4", "3", "3", "5", "7", "FREE",
            "2", "5", "4", "ACE", "6", "6", "2", "5",
            "2", "2", "4", "FREE", "2", "4", "3", "4",
            "3", "5", "5", "POWER", "3", "4", "2", "7",
            "POWER", "3", "3", "3", "5", "3", "4", "5",
            "3", "3", "2", "2", "4", "3", "6", "3",
            "6", "2", "6", "8", "POWER", "7", "POWER", "FREE",
            "2", "4", "5", "4", "6", "POWER", "3", "3"
        ]
    ];

    // Preserve special-symbol positions while dispersing the removed letter stops.
    // Each current strip has 26 letters, ten chips/rings and nine cash symbols.
    // Letter weight is balanced across reels; feature injection determines visible share.
    private static readonly (int Index, string Before, string After)[][] CurrentReplacements =
    [
        [(1, "4", "7"), (6, "4", "8"), (10, "2", "8"), (12, "5", "7"), (15, "3", "6"),
            (17, "3", "7"), (19, "4", "8"), (21, "3", "7"), (25, "3", "6"), (28, "3", "7"),
            (30, "3", "7"), (34, "4", "7"), (38, "2", "8"), (41, "5", "8"), (44, "5", "6"),
            (47, "2", "8"), (49, "3", "6"), (61, "2", "8"), (63, "3", "8")],
        [(2, "5", "8"), (5, "3", "8"), (10, "3", "6"), (13, "2", "8"), (15, "3", "7"),
            (18, "2", "7"), (21, "3", "8"), (26, "4", "8"), (29, "3", "7"), (31, "3", "8"),
            (36, "3", "7"), (38, "5", "7"), (42, "2", "7"), (45, "4", "6"), (48, "2", "7"),
            (50, "2", "8"), (54, "4", "6"), (59, "3", "8"), (63, "3", "6")],
        [(2, "3", "6"), (4, "3", "6"), (7, "5", "6"), (9, "4", "8"), (11, "2", "7"),
            (13, "2", "6"), (20, "2", "7"), (25, "3", "8"), (28, "3", "7"), (31, "5", "7"),
            (35, "3", "8"), (38, "2", "8"), (41, "5", "8"), (43, "3", "8"), (50, "4", "7"),
            (53, "4", "8"), (58, "2", "7"), (60, "3", "8"), (63, "3", "7")],
        [(2, "4", "7"), (5, "3", "6"), (7, "4", "6"), (13, "4", "7"), (15, "4", "8"),
            (17, "2", "7"), (26, "2", "8"), (28, "2", "8"), (31, "3", "6"), (34, "3", "7"),
            (37, "5", "7"), (39, "3", "7"), (44, "3", "8"), (46, "5", "8"), (50, "3", "8"),
            (52, "3", "7"), (57, "2", "6"), (61, "3", "8"), (63, "3", "8")],
        [(0, "2", "7"), (4, "3", "6"), (8, "2", "8"), (10, "4", "8"), (15, "5", "7"),
            (18, "4", "8"), (22, "3", "8"), (25, "5", "7"), (28, "3", "6"), (30, "2", "6"),
            (33, "3", "8"), (35, "3", "7"), (38, "4", "7"), (41, "3", "8"), (44, "4", "8"),
            (47, "3", "8"), (56, "2", "7"), (58, "5", "6"), (62, "3", "7")]
    ];

    public static ReelSetDefinition Create() => new()
    {
        Id = Id,
        SymbolSetId = MidnightVaultSlotDefinitions.SymbolSetId,
        Reels = HistoricalReels.Select(strip => strip.ToList()).ToList()
    };

    public static ReelSetDefinition CreateCurrent()
    {
        var reels = HistoricalReels.Select(strip => strip.ToList()).ToList();
        for (var reel = 0; reel < reels.Count; reel++)
        {
            foreach (var (index, before, after) in CurrentReplacements[reel])
            {
                if (reels[reel][index] != before)
                    throw new InvalidOperationException("A frozen Midnight Vault reel replacement no longer matches.");
                reels[reel][index] = after;
            }
        }

        return new ReelSetDefinition
        {
            Id = CurrentId,
            SymbolSetId = MidnightVaultSlotDefinitions.SymbolSetId,
            Reels = reels
        };
    }
}
