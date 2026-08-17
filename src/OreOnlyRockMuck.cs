using HarmonyLib;
using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace InterestingMeMaterialNeedsFurnacePatch;

public sealed class OreOnlyRockMuckConfig
{
    public bool OnlyOreBlocksCreateMuck { get; set; } = true;
}

internal static class OreOnlyRockMuck
{
    internal const string ConfigFileName = "imeolendrilpatch.json";
    private const string TargetTypeName = "IME.BlockBehaviorDropMuck";
    private const string TargetMethodName = "BuildComposition";

    private static bool enabled = true;

    private static readonly HashSet<string> SupportedRockCodes = new(StringComparer.Ordinal)
    {
        // Vintage Story rock entries from InterestingME 1.0.16.
        "game:rock-granite",
        "game:rock-andesite",
        "game:rock-basalt",
        "game:rock-obsidian",
        "game:rock-peridotite",
        "game:rock-kimberlite",
        "game:rock-chalk",
        "game:rock-chert",
        "game:rock-conglomerate",
        "game:rock-limestone",
        "game:rock-travertine",
        "game:rock-halite",
        "game:rock-sandstone",
        "game:rock-claystone",
        "game:rock-bauxite",
        "game:rock-phyllite",
        "game:rock-shale",
        "game:rock-slate",
        "game:rock-suevite",
        "game:rock-redmarble",
        "game:rock-whitemarble",
        "game:rock-greenmarble",

        // Material Needs Geology 2.0.10 entries.
        "game:rock-arenite",
        "game:rock-arkose",
        "game:rock-komatiite",
        "game:rock-marl",
        "game:rock-monzonite",
        "game:rock-pyroxenite",
        "game:rock-serpentinite",
        "game:rock-syenite",
        "game:rock-tufa",
        "game:rock-wacke",

        // Geology Additions 1.4.8 entries.
        "game:rock-amphibolite",
        "game:rock-diorite",
        "game:rock-dolostone",
        "game:rock-gabbro",
        "game:rock-gneiss",
        "game:rock-jade",
        "game:rock-jasper",
        "game:rock-migmatite",
        "game:rock-mudstone",
        "game:rock-pumice",
        "game:rock-quartzite",
        "game:rock-rhyolite",
        "game:rock-schist",
        "game:rock-siltstone"
    };

    internal static void LoadConfig(ICoreAPI api)
    {
        try
        {
            var config = api.LoadModConfig<OreOnlyRockMuckConfig>(ConfigFileName);
            if (config is null)
            {
                config = new OreOnlyRockMuckConfig();
                api.StoreModConfig(config, ConfigFileName);
            }

            enabled = config.OnlyOreBlocksCreateMuck;
        }
        catch (Exception exception)
        {
            enabled = true;
            api.Logger.Warning(
                "[imeolendrilpatch] Could not load {0}; using OnlyOreBlocksCreateMuck=true ({1}).",
                ConfigFileName, exception.Message);
        }
    }

    internal static void TryInitialize(Harmony harmony, ICoreAPI api)
    {
        var target = ResolveTargetMethod();
        if (target is null)
        {
            api.Logger.Warning(
                "[imeolendrilpatch] Could not find exact internal static {0}.{1}(IWorldAccessor, NatFloat, NatFloat, string, string) returning IME.MuckComposition; rock muck filtering is disabled.",
                TargetTypeName, TargetMethodName);
            return;
        }

        try
        {
            harmony.Patch(
                target,
                prefix: new HarmonyMethod(typeof(OreOnlyRockMuck), nameof(FilterStoneLayers)));
        }
        catch (Exception exception)
        {
            harmony.Unpatch(target, HarmonyPatchType.All, harmony.Id);
            api.Logger.Warning(
                "[imeolendrilpatch] Failed to patch {0}.{1}; rock muck filtering is disabled ({2}).",
                TargetTypeName, TargetMethodName, exception.Message);
        }
    }

    private static MethodInfo? ResolveTargetMethod()
    {
        var targetType = AccessTools.TypeByName(TargetTypeName);
        var compositionType = AccessTools.TypeByName("IME.MuckComposition");
        if (targetType is null || compositionType is null) return null;

        var method = AccessTools.Method(
            targetType,
            TargetMethodName,
            new[] { typeof(IWorldAccessor), typeof(NatFloat), typeof(NatFloat), typeof(string), typeof(string) });
        return method is not null && method.IsAssembly && method.IsStatic && method.ReturnType == compositionType
            ? method
            : null;
    }

    private static void FilterStoneLayers(ref NatFloat? stoneLayers, string? oreCode, string? stoneCode)
    {
        if (ShouldRemoveRockMuck(enabled, oreCode, stoneCode)) stoneLayers = null;
    }

    internal static bool ShouldRemoveRockMuck(bool mechanismEnabled, string? oreCode, string? stoneCode)
    {
        return mechanismEnabled &&
               string.IsNullOrEmpty(oreCode) &&
               stoneCode is not null &&
               SupportedRockCodes.Contains(stoneCode);
    }
}
