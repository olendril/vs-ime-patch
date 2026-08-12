using HarmonyLib;
using System.Reflection;
using Vintagestory.API.Common;

namespace InterestingMeMaterialNeedsFurnacePatch;

/// <summary>
/// Extends InterestingME's furnace validation for the exact compatible full
/// blocks supplied by Material Needs and Bricklayers.
/// </summary>
public sealed class MaterialNeedsFurnacePatchSystem : ModSystem
{
    private const string HarmonyId = "ime-olendril-patch";
    private const string LowTempTargetTypeName = "IME.BlockEntityLowTempFurnaceDoor";
    private const string LowTempTargetMethodName = "IsValidTierBrick";
    private const string RoastingTargetTypeName = "IME.BlockEntityRoastingFurnaceDoor";
    private const string RoastingTargetMethodName = "IsValidBrick";

    private Harmony? harmony;

    private static readonly HashSet<string> MaterialNeedsTierOneMudbrickPaths = new(StringComparer.Ordinal)
    {
        "brownmudbrick-light",
        "brownmudbrick-dark",
        "graymudbrick-light",
        "graymudbrick-dark",
        "oxbloodmudbrick-light",
        "oxbloodmudbrick-dark",
        "oystermudbrick-light",
        "oystermudbrick-dark",
        "redmudbrick-light",
        "redmudbrick-dark"
    };

    private static readonly HashSet<string> BricklayersGlazedBrickPaths = new(StringComparer.Ordinal)
    {
        "glazedbricks-clear-black", "glazedbricks-clear-blue", "glazedbricks-clear-brown",
        "glazedbricks-clear-gold", "glazedbricks-clear-green", "glazedbricks-clear-greenblue",
        "glazedbricks-clear-lapislazuli", "glazedbricks-clear-malachite", "glazedbricks-clear-orange",
        "glazedbricks-clear-pink", "glazedbricks-clear-purple", "glazedbricks-clear-red",
        "glazedbricks-clear-redbrown", "glazedbricks-clear-white", "glazedbricks-clear-yellow",
        "glazedbricks-milky-black", "glazedbricks-milky-blue", "glazedbricks-milky-brown",
        "glazedbricks-milky-gold", "glazedbricks-milky-green", "glazedbricks-milky-greenblue",
        "glazedbricks-milky-lapislazuli", "glazedbricks-milky-malachite", "glazedbricks-milky-orange",
        "glazedbricks-milky-pink", "glazedbricks-milky-purple", "glazedbricks-milky-red",
        "glazedbricks-milky-redbrown", "glazedbricks-milky-white", "glazedbricks-milky-yellow"
    };

    public override void Start(ICoreAPI api)
    {
        harmony = new Harmony(HarmonyId);
        PatchTarget(
            api,
            LowTempTargetTypeName,
            LowTempTargetMethodName,
            new[] { typeof(int), typeof(string) },
            nameof(IsMaterialNeedsTierOneMudbrick),
            "(int, string)");
        PatchTarget(
            api,
            LowTempTargetTypeName,
            LowTempTargetMethodName,
            new[] { typeof(int), typeof(string) },
            nameof(IsBricklayersTierTwoGlazedBrick),
            "(int, string)");
        PatchTarget(
            api,
            RoastingTargetTypeName,
            RoastingTargetMethodName,
            new[] { typeof(string) },
            nameof(IsRoastingGlazedBrick),
            "(string)");
    }

    public override void Dispose()
    {
        harmony?.UnpatchAll(HarmonyId);
        harmony = null;
        base.Dispose();
    }

    private void PatchTarget(
        ICoreAPI api,
        string targetTypeName,
        string targetMethodName,
        Type[] parameterTypes,
        string postfixName,
        string signature)
    {
        var targetMethod = ResolveTargetMethod(targetTypeName, targetMethodName, parameterTypes);
        if (targetMethod is null)
        {
            api.Logger.Warning(
                "[{0}] Could not find exact private static {1}.{2}{3} returning bool; skipped only this compatibility patch.",
                HarmonyId, targetTypeName, targetMethodName, signature);
            return;
        }

        try
        {
            harmony!.Patch(targetMethod, postfix: new HarmonyMethod(typeof(MaterialNeedsFurnacePatchSystem), postfixName));
        }
        catch (Exception exception)
        {
            harmony!.Unpatch(targetMethod, HarmonyPatchType.All, HarmonyId);
            api.Logger.Warning(
                "[{0}] Failed to patch {1}.{2}{3}; skipped only this compatibility patch ({4}).",
                HarmonyId, targetTypeName, targetMethodName, signature, exception.Message);
        }
    }

    private static MethodInfo? ResolveTargetMethod(string typeName, string methodName, Type[] parameterTypes)
    {
        var type = AccessTools.TypeByName(typeName);
        var method = type is null ? null : AccessTools.Method(type, methodName, parameterTypes);
        if (method is null || !method.IsPrivate || !method.IsStatic || method.ReturnType != typeof(bool)) return null;

        var actualParameters = method.GetParameters();
        return actualParameters.Length == parameterTypes.Length &&
               actualParameters.Select(parameter => parameter.ParameterType).SequenceEqual(parameterTypes)
            ? method
            : null;
    }

    private static void IsMaterialNeedsTierOneMudbrick(int tier, string path, ref bool __result)
    {
        // Never override a brick already accepted by InterestingME.
        if (__result || tier != 1 || string.IsNullOrEmpty(path)) return;

        // InterestingME passes AssetLocation.Path here, which intentionally omits
        // the domain. The exact names below are the Material Needs 2.0.0 full
        // block paths; slabs and every unrelated path are excluded by equality.
        __result = MaterialNeedsTierOneMudbrickPaths.Contains(path);
    }

    private static void IsBricklayersTierTwoGlazedBrick(int tier, string path, ref bool __result)
    {
        if (__result || tier != 2 || string.IsNullOrEmpty(path)) return;
        __result = BricklayersGlazedBrickPaths.Contains(path);
    }

    private static void IsRoastingGlazedBrick(string path, ref bool __result)
    {
        if (__result || string.IsNullOrEmpty(path)) return;
        __result = BricklayersGlazedBrickPaths.Contains(path);
    }
}
