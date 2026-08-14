using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace InterestingMeMaterialNeedsFurnacePatch;

/// <summary>
/// Synchronizes the binary power state of InterestingME machines and adds it
/// to the placed-block information for the four powered machine blocks.
/// </summary>
internal static class ImePowerTooltip
{
    private const string HarmonyId = "ime-olendril-patch";
    private const string PoweredMachineTypeName = "IME.PoweredMachineBlockEntity";
    private const string DescribedBlockTypeName = "IME.BlockIMEDescribed";
    private const string FlatConveyorTypeName = "IME.BlockConveyorFlat";
    private const string SplitConveyorTypeName = "IME.BlockConveyorSplit";
    private const string BucketLiftOutputTypeName = "IME.BlockBucketLiftOutput";
    private static readonly string[] PoweredMachineSubclassTypeNames =
    {
        "IME.BlockEntityJawCrusher",
        "IME.BlockEntityConveyorFlat",
        "IME.BlockEntityConveyorSplit",
        "IME.BlockEntityBucketLiftOutput"
    };
    private const string ApplyPowerStateMethodName = "ApplyPowerState";
    private const string PlacedBlockInfoMethodName = "GetPlacedBlockInfo";
    private const string ToTreeAttributesMethodName = "ToTreeAttributes";
    private const string FromTreeAttributesMethodName = "FromTreeAttributes";

    internal const string PowerTreeKey = "ime-olendril-patch:ime-powered";
    internal const string PoweredLangKey = "ime-powered";
    internal const string UnpoweredLangKey = "ime-unpowered";
    private const string PoweredFallback = "IME power: Powered";
    private const string UnpoweredFallback = "IME power: Unpowered";

    private static ConditionalWeakTable<object, CachedPowerState> cachedStates = new();
    private static Compatibility? compatibility;
    private static readonly List<MethodBase> PatchedMethods = new();

    internal static bool TryInitialize(Harmony harmony, ICoreAPI api)
    {
        if (compatibility is not null) return true;

        var resolved = ResolveCompatibility();
        if (resolved is null)
        {
            api.Logger.Warning(
                "[{0}] InterestingME 1.0.16 powered-machine tooltip compatibility checks failed; powered status tooltips were disabled.",
                HarmonyId);
            return false;
        }

        try
        {
            Patch(harmony, resolved.ApplyPowerState, nameof(ApplyPowerStatePrefix), nameof(ApplyPowerStatePostfix));
            Patch(harmony, resolved.ToTreeAttributes, postfixName: nameof(ToTreeAttributesPostfix));
            Patch(harmony, resolved.FromTreeAttributes, postfixName: nameof(FromTreeAttributesPostfix));

            foreach (var tooltipMethod in resolved.TooltipMethods)
                Patch(harmony, tooltipMethod, postfixName: nameof(GetPlacedBlockInfoPostfix));

            compatibility = resolved;
            return true;
        }
        catch (Exception exception)
        {
            Rollback(harmony);
            api.Logger.Warning(
                "[{0}] InterestingME 1.0.16 powered-machine tooltip registration failed; powered status tooltips were disabled ({1}).",
                HarmonyId,
                exception.Message);
            return false;
        }
    }

    internal static void Disable(Harmony? harmony = null)
    {
        if (harmony is not null)
        {
            foreach (var target in PatchedMethods)
            {
                try
                {
                    harmony.Unpatch(target, HarmonyPatchType.All, HarmonyId);
                }
                catch
                {
                    // The owning mod system also performs a final UnpatchAll.
                }
            }
        }

        compatibility = null;
        cachedStates = new ConditionalWeakTable<object, CachedPowerState>();
        PatchedMethods.Clear();
    }

    private static void Patch(
        Harmony harmony,
        MethodInfo target,
        string? prefixName = null,
        string? postfixName = null)
    {
        PatchedMethods.Add(target);
        harmony.Patch(
            target,
            prefix: prefixName is null ? null : new HarmonyMethod(typeof(ImePowerTooltip), prefixName),
            postfix: postfixName is null ? null : new HarmonyMethod(typeof(ImePowerTooltip), postfixName));
    }

    private static void Rollback(Harmony harmony)
    {
        foreach (var target in PatchedMethods)
        {
            try
            {
                harmony.Unpatch(target, HarmonyPatchType.All, HarmonyId);
            }
            catch
            {
                // The original startup exception is the only compatibility warning.
            }
        }

        PatchedMethods.Clear();
        compatibility = null;
        cachedStates = new ConditionalWeakTable<object, CachedPowerState>();
    }

    private static Compatibility? ResolveCompatibility()
    {
        var poweredMachineType = AccessTools.TypeByName(PoweredMachineTypeName);
        var describedBlockType = AccessTools.TypeByName(DescribedBlockTypeName);
        var flatConveyorType = AccessTools.TypeByName(FlatConveyorTypeName);
        var splitConveyorType = AccessTools.TypeByName(SplitConveyorTypeName);
        var bucketLiftOutputType = AccessTools.TypeByName(BucketLiftOutputTypeName);
        if (poweredMachineType is null || describedBlockType is null || flatConveyorType is null ||
            splitConveyorType is null || bucketLiftOutputType is null)
            return null;

        var poweredMachineSubclasses = PoweredMachineSubclassTypeNames
            .Select(AccessTools.TypeByName)
            .ToArray();
        if (poweredMachineSubclasses.Any(type => type is null || type.BaseType != poweredMachineType))
            return null;

        var applyPowerState = ResolveMethod(
            poweredMachineType,
            ApplyPowerStateMethodName,
            BindingFlags.Instance | BindingFlags.Public,
            typeof(void),
            typeof(float), typeof(bool), typeof(BlockPos));
        var isPowered = poweredMachineType.GetProperty(
            "IsPowered",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        var toTreeAttributes = ResolveMethod(
            typeof(BlockEntity),
            ToTreeAttributesMethodName,
            BindingFlags.Instance | BindingFlags.Public,
            typeof(void),
            typeof(ITreeAttribute));
        var fromTreeAttributes = ResolveMethod(
            typeof(BlockEntity),
            FromTreeAttributesMethodName,
            BindingFlags.Instance | BindingFlags.Public,
            typeof(void),
            typeof(ITreeAttribute), typeof(IWorldAccessor));
        var tooltipMethods = new[]
        {
            ResolveMethod(describedBlockType, PlacedBlockInfoMethodName, BindingFlags.Instance | BindingFlags.Public,
                typeof(string), typeof(IWorldAccessor), typeof(BlockPos), typeof(IPlayer)),
            ResolveMethod(flatConveyorType, PlacedBlockInfoMethodName, BindingFlags.Instance | BindingFlags.Public,
                typeof(string), typeof(IWorldAccessor), typeof(BlockPos), typeof(IPlayer)),
            ResolveMethod(splitConveyorType, PlacedBlockInfoMethodName, BindingFlags.Instance | BindingFlags.Public,
                typeof(string), typeof(IWorldAccessor), typeof(BlockPos), typeof(IPlayer)),
            ResolveMethod(bucketLiftOutputType, PlacedBlockInfoMethodName, BindingFlags.Instance | BindingFlags.Public,
                typeof(string), typeof(IWorldAccessor), typeof(BlockPos), typeof(IPlayer))
        };

        if (applyPowerState is null || applyPowerState.DeclaringType != poweredMachineType ||
            isPowered is null || isPowered.PropertyType != typeof(bool) ||
            isPowered.GetMethod is null || !isPowered.GetMethod.IsFamily || toTreeAttributes is null ||
            toTreeAttributes.DeclaringType != typeof(BlockEntity) || fromTreeAttributes is null ||
            fromTreeAttributes.DeclaringType != typeof(BlockEntity) || tooltipMethods.Any(method => method is null) ||
            tooltipMethods.Zip(
                new[] { describedBlockType, flatConveyorType, splitConveyorType, bucketLiftOutputType })
                .Any(pair => pair.First!.DeclaringType != pair.Second))
            return null;

        return new Compatibility(
            poweredMachineType,
            isPowered,
            applyPowerState,
            toTreeAttributes,
            fromTreeAttributes,
            tooltipMethods!);
    }

    private static MethodInfo? ResolveMethod(
        Type type,
        string name,
        BindingFlags bindingFlags,
        Type returnType,
        params Type[] parameterTypes)
    {
        var method = type.GetMethod(name, bindingFlags, binder: null, parameterTypes, modifiers: null);
        if (method is null || method.ReturnType != returnType || !method.IsVirtual) return null;

        var parameters = method.GetParameters();
        return parameters.Length == parameterTypes.Length &&
               parameters.Select(parameter => parameter.ParameterType).SequenceEqual(parameterTypes)
            ? method
            : null;
    }

    private static void ApplyPowerStatePrefix(object __instance, ref PowerStateSnapshot? __state)
    {
        if (TryReadOperationalPowerState(__instance, out var powered))
            __state = new PowerStateSnapshot(powered);
    }

    private static void ApplyPowerStatePostfix(object __instance, PowerStateSnapshot? __state)
    {
        if (!TryReadOperationalPowerState(__instance, out var powered)) return;

        CachePowerState(__instance, powered);
        if (__state is null || !HasBinaryPowerStateChanged(__state.Powered, powered) ||
            __instance is not BlockEntity blockEntity ||
            blockEntity.Api?.Side != EnumAppSide.Server)
            return;

        blockEntity.MarkDirty(false);
    }

    private static void ToTreeAttributesPostfix(object __instance, ITreeAttribute tree)
    {
        if (!IsPoweredMachine(__instance) || !TryReadOperationalPowerState(__instance, out var powered)) return;

        CachePowerState(__instance, powered);
        tree.SetBool(PowerTreeKey, powered);
    }

    private static void FromTreeAttributesPostfix(object __instance, ITreeAttribute tree)
    {
        if (!IsPoweredMachine(__instance) || tree[PowerTreeKey] is null) return;

        CachePowerState(__instance, tree.GetBool(PowerTreeKey, false));
    }

    private static void GetPlacedBlockInfoPostfix(
        object __instance,
        IWorldAccessor world,
        BlockPos pos,
        IPlayer forPlayer,
        ref string __result)
    {
        if (world?.BlockAccessor is null || pos is null) return;

        var blockEntity = world.BlockAccessor.GetBlockEntity(pos);
        if (!IsPoweredMachine(blockEntity) || !TryGetCachedPowerState(blockEntity, out var powered)) return;

        __result = AppendPowerStatus(__result, powered);
    }

    private static bool IsPoweredMachine(object? value) =>
        value is not null && compatibility?.PoweredMachineType.IsInstanceOfType(value) == true;

    private static bool TryReadOperationalPowerState(object value, out bool powered)
    {
        powered = false;
        var resolved = compatibility;
        if (resolved is null || !resolved.PoweredMachineType.IsInstanceOfType(value)) return false;

        try
        {
            powered = (bool)resolved.IsPoweredProperty.GetValue(value)!;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void CachePowerState(object value, bool powered)
    {
        cachedStates.Remove(value);
        cachedStates.Add(value, new CachedPowerState(powered));
    }

    private static bool TryGetCachedPowerState(object value, out bool powered)
    {
        if (cachedStates.TryGetValue(value, out var cached))
        {
            powered = cached.Powered;
            return true;
        }

        return TryReadOperationalPowerState(value, out powered);
    }

    internal static string AppendPowerStatus(string? existing, bool powered)
    {
        string status;
        try
        {
            status = Lang.Get(powered ? PoweredLangKey : UnpoweredLangKey);
            if (string.Equals(status, powered ? PoweredLangKey : UnpoweredLangKey, StringComparison.Ordinal))
                status = powered ? PoweredFallback : UnpoweredFallback;
        }
        catch
        {
            status = powered ? PoweredFallback : UnpoweredFallback;
        }
        if (existing is not null && (string.Equals(existing, status, StringComparison.Ordinal) ||
            existing.EndsWith("\n" + status, StringComparison.Ordinal)))
            return existing;

        return string.IsNullOrEmpty(existing) ? status : existing + "\n" + status;
    }

    internal static bool HasBinaryPowerStateChanged(bool previous, bool current) => previous != current;

    private sealed class Compatibility
    {
        internal Compatibility(
            Type poweredMachineType,
            PropertyInfo isPoweredProperty,
            MethodInfo applyPowerState,
            MethodInfo toTreeAttributes,
            MethodInfo fromTreeAttributes,
            IEnumerable<MethodInfo> tooltipMethods)
        {
            PoweredMachineType = poweredMachineType;
            IsPoweredProperty = isPoweredProperty;
            ApplyPowerState = applyPowerState;
            ToTreeAttributes = toTreeAttributes;
            FromTreeAttributes = fromTreeAttributes;
            TooltipMethods = tooltipMethods.ToArray();
        }

        internal Type PoweredMachineType { get; }
        internal PropertyInfo IsPoweredProperty { get; }
        internal MethodInfo ApplyPowerState { get; }
        internal MethodInfo ToTreeAttributes { get; }
        internal MethodInfo FromTreeAttributes { get; }
        internal IReadOnlyList<MethodInfo> TooltipMethods { get; }
    }

    private sealed class CachedPowerState
    {
        internal CachedPowerState(bool powered) => Powered = powered;

        internal bool Powered { get; }
    }

    private sealed class PowerStateSnapshot
    {
        internal PowerStateSnapshot(bool powered) => Powered = powered;

        internal bool Powered { get; }
    }
}
