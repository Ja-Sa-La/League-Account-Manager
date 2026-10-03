namespace League_Account_Manager.Misc;

/// <summary>
///     The current rune reforged tree. Perk and shard ids are stable across patches;
///     names follow the live tree so a page can be built without the League client running.
/// </summary>
internal static class RuneCatalog
{
    internal const int StatOffense = 5001;
    internal const int StatFlex = 5002;
    internal const int StatDefense = 5003;

    internal static readonly RuneStyle[] Styles =
    [
        Style(8000, "Precision",
            Slot(8005, "Press the Attack", 8008, "Lethal Tempo", 8021, "Fleet Footwork", 8010, "Conqueror"),
            Slot(9101, "Absorb Life", 9111, "Triumph", 8009, "Presence of Mind"),
            Slot(9104, "Legend: Alacrity", 9105, "Legend: Haste", 9103, "Legend: Bloodline"),
            Slot(8014, "Coup de Grace", 8017, "Cut Down", 8299, "Last Stand")),
        Style(8100, "Domination",
            Slot(8112, "Electrocute", 8128, "Dark Harvest", 9923, "Hail of Blades"),
            Slot(8126, "Cheap Shot", 8139, "Taste of Blood", 8143, "Sudden Impact"),
            Slot(8137, "Sixth Sense", 8140, "Grisly Mementos", 8141, "Deep Ward"),
            Slot(8135, "Treasure Hunter", 8105, "Relentless Hunter", 8106, "Ultimate Hunter")),
        Style(8200, "Sorcery",
            Slot(8214, "Summon Aery", 8229, "Arcane Comet", 8230, "Stormraider's Surge", 8992, "Deathfire Touch"),
            Slot(8224, "Axiom Arcanist", 8226, "Manaflow Band", 8275, "Nimbus Cloak"),
            Slot(8210, "Transcendence", 8234, "Celerity", 8233, "Absolute Focus"),
            Slot(8237, "Scorch", 8232, "Waterwalking", 8236, "Gathering Storm")),
        Style(8300, "Inspiration",
            Slot(8351, "Glacial Augment", 8360, "Unsealed Spellbook", 8369, "First Strike"),
            Slot(8306, "Hextech Flashtraption", 8304, "Magical Footwear", 8321, "Cash Back"),
            Slot(8313, "Triple Tonic", 8352, "Time Warp Tonic", 8345, "Biscuit Delivery"),
            Slot(8347, "Cosmic Insight", 8410, "Approach Velocity", 8316, "Jack Of All Trades")),
        Style(8400, "Resolve",
            Slot(8437, "Grasp of the Undying", 8439, "Aftershock", 8465, "Guardian"),
            Slot(8446, "Demolish", 8463, "Font of Life", 8401, "Shield Bash"),
            Slot(8429, "Conditioning", 8444, "Second Wind", 8473, "Bone Plating"),
            Slot(8451, "Overgrowth", 8453, "Revitalize", 8242, "Unflinching"))
    ];

    internal static readonly RuneShardRow[] ShardRows =
    [
        new(StatOffense, "Offense",
        [
            new(5008, "Adaptive Force"), new(5005, "Attack Speed"), new(5007, "Ability Haste")
        ]),
        new(StatFlex, "Flex",
        [
            new(5008, "Adaptive Force"), new(5010, "Move Speed"), new(5001, "Health Scaling")
        ]),
        new(StatDefense, "Defense",
        [
            new(5001, "Health"), new(5011, "Tenacity"), new(5013, "Health Scaling")
        ])
    ];

    internal static RuneStyle? FindStyle(int styleId)
    {
        foreach (var style in Styles)
        {
            if (style.Id == styleId)
                return style;
        }

        return null;
    }

    internal static string StyleName(int styleId) => FindStyle(styleId)?.Name ?? $"Style {styleId}";

    internal static string PerkName(int perkId)
    {
        foreach (var style in Styles)
        foreach (var slot in style.Slots)
        foreach (var perk in slot)
        {
            if (perk.Id == perkId)
                return perk.Name;
        }

        foreach (var row in ShardRows)
        foreach (var shard in row.Shards)
        {
            if (shard.Id == perkId)
                return shard.Name;
        }

        return $"Rune {perkId}";
    }

    internal static bool ContainsPerk(int styleId, int perkId)
    {
        var style = FindStyle(styleId);
        if (style == null)
            return false;

        foreach (var slot in style.Slots)
        foreach (var perk in slot)
        {
            if (perk.Id == perkId)
                return true;
        }

        return false;
    }

    internal static RunePage DefaultPage()
    {
        var precision = Styles[0];
        var domination = Styles[1];
        return new RunePage
        {
            Name = "League Account Manager",
            PrimaryStyleId = precision.Id,
            SubStyleId = domination.Id,
            PerkIds = [precision.Slots[0][0].Id, precision.Slots[1][0].Id, precision.Slots[2][0].Id, precision.Slots[3][0].Id],
            SecondaryPerkIds = [domination.Slots[1][0].Id, domination.Slots[2][0].Id],
            StatShardIds = [5008, 5008, 5001]
        };
    }

    private static RuneStyle Style(int id, string name, params RunePerk[][] slots) => new(id, name, slots);

    private static RunePerk[] Slot(int id1, string name1, int id2, string name2, int id3, string name3) =>
        [new(id1, name1), new(id2, name2), new(id3, name3)];

    private static RunePerk[] Slot(int id1, string name1, int id2, string name2, int id3, string name3, int id4,
        string name4) => [new(id1, name1), new(id2, name2), new(id3, name3), new(id4, name4)];
}

internal sealed record RunePerk(int Id, string Name);

internal sealed record RuneStyle(int Id, string Name, RunePerk[][] Slots);

internal sealed record RuneShard(int Id, string Name);

internal sealed record RuneShardRow(int StyleId, string Name, RuneShard[] Shards);

internal sealed class RunePage
{
    public string Name { get; set; } = "League Account Manager";
    public int PrimaryStyleId { get; set; }
    public int SubStyleId { get; set; }
    public int[] PerkIds { get; set; } = new int[4];
    public int[] SecondaryPerkIds { get; set; } = new int[2];
    public int[] StatShardIds { get; set; } = [5008, 5008, 5001];

    public RunePage Clone() => new()
    {
        Name = Name,
        PrimaryStyleId = PrimaryStyleId,
        SubStyleId = SubStyleId,
        PerkIds = (int[])PerkIds.Clone(),
        SecondaryPerkIds = (int[])SecondaryPerkIds.Clone(),
        StatShardIds = (int[])StatShardIds.Clone()
    };

    public string Describe()
    {
        var primary = string.Join(", ", PerkIds.Select(RuneCatalog.PerkName));
        var secondary = string.Join(", ", SecondaryPerkIds.Select(RuneCatalog.PerkName));
        var shards = string.Join(", ", StatShardIds.Select(RuneCatalog.PerkName));
        return $"{RuneCatalog.StyleName(PrimaryStyleId)}: {primary} | " +
               $"{RuneCatalog.StyleName(SubStyleId)}: {secondary} | {shards}";
    }
}
