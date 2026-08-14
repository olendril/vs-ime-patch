using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Config;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;

namespace InterestingMeMaterialNeedsFurnacePatch;

/// <summary>
/// Adds the slow, server-authoritative hammer interaction for a single coarse
/// InterestingME muck layer. The InterestingME types are resolved at runtime
/// so an incompatible upstream assembly can disable this feature alone.
/// </summary>
public sealed class ManualMuckCrusher : CollectibleBehavior
{
    internal const string BehaviorName = "ManualMuckCrusher";
    internal const string HarmonyId = "ime-olendril-patch";
    internal const string HammerTargetTypeName = "Vintagestory.GameContent.ItemHammer";
    internal const string ProgressTreeKey = "ime-olendril-patch:manual-muck-crusher-strikes";
    internal const int RequiredStrikes = 5;

    private static readonly HashSet<string> EligibleHammerPaths = new(StringComparer.Ordinal)
    {
        "hammer-tinbronze",
        "hammer-bismuthbronze",
        "hammer-blackbronze",
        "hammer-iron",
        "hammer-meteoriciron",
        "hammer-steel"
    };

    private static readonly ConditionalWeakTable<object, ManualMuckCrusherProgress> ProgressByPile = new();
    private static ManualMuckCrusherCompatibility? compatibility;
    private static Harmony? harmony;
    private static ICoreAPI? api;
    private static readonly List<MethodBase> PatchedSerializationTargets = new();

    public ManualMuckCrusher(CollectibleObject collObj) : base(collObj)
    {
    }

    public override void OnHeldAttackStart(
        ItemSlot slot,
        EntityAgent byEntity,
        BlockSelection blockSel,
        EntitySelection entitySel,
        ref EnumHandHandling handHandling,
        ref EnumHandling handling)
    {
        TryHandleHeldAttack(slot, byEntity, blockSel, entitySel, ref handHandling, ref handling);
    }

    private static bool HeldAttackPrefix(
        ItemSlot slot,
        EntityAgent byEntity,
        BlockSelection blockSel,
        EntitySelection entitySel,
        ref EnumHandHandling handling)
    {
        var behaviorHandling = EnumHandling.PassThrough;
        return !TryHandleHeldAttack(slot, byEntity, blockSel, entitySel, ref handling, ref behaviorHandling);
    }

    private static bool TryHandleHeldAttack(
        ItemSlot slot,
        EntityAgent byEntity,
        BlockSelection blockSel,
        EntitySelection entitySel,
        ref EnumHandHandling handHandling,
        ref EnumHandling handling)
    {
        if (blockSel?.Position is null || slot.Itemstack?.Collectible?.Code is not AssetLocation hammerCode)
            return false;

        if (!byEntity.Controls.Sneak) return false;

        var targetBlock = byEntity.World.BlockAccessor.GetBlock(blockSel.Position);
        if (targetBlock is null) return false;

        var resolvedCompatibility = compatibility;
        if (resolvedCompatibility is null)
        {
            if (!IsMuckPileBlockCode(targetBlock.Code)) return false;

            PreventDefault(ref handHandling, ref handling);
                SendServerHudError(byEntity, "ime-message-manualmuck-disabled");
            return true;
        }

        if (!resolvedCompatibility.MuckBlockType.IsInstanceOfType(targetBlock))
        {
            if (!IsMuckPileBlockCode(targetBlock.Code)) return false;

            PreventDefault(ref handHandling, ref handling);
            SendServerHudError(byEntity, "ime-message-manualmuck-no-pile");
            return true;
        }
        if (!IsEligibleHammerCode(hammerCode))
        {
            PreventDefault(ref handHandling, ref handling);
            SendServerHudError(byEntity, "ime-message-manualmuck-wrong-hammer");
            return true;
        }

        var blockProcessingVariant = resolvedCompatibility.GetBlockProcessingVariant(targetBlock);
        var blockLayerCount = resolvedCompatibility.GetBlockLayerCount(targetBlock);
        if (!IsCoarseSingleLayerBlock(
                byEntity.Controls.Sneak,
                "interestingme",
                "muckpile",
                blockProcessingVariant,
                blockLayerCount))
        {
            PreventDefault(ref handHandling, ref handling);
            if (byEntity.World.Side == EnumAppSide.Server)
            {
                SendServerHudError(
                    byEntity,
                    string.Equals(blockProcessingVariant, "coarse", StringComparison.Ordinal)
                        ? "ime-message-manualmuck-one-layer"
                        : "ime-message-manualmuck-not-coarse");
            }

            return true;
        }

        // Keep the client from running the vanilla attack. It never
        // advances state or damages the item; the server is the only authority.
        PreventDefault(ref handHandling, ref handling);

        if (byEntity.World.Side != EnumAppSide.Server) return true;

        var blockEntity = byEntity.World.BlockAccessor.GetBlockEntity(blockSel.Position);
        if (blockEntity is null || !resolvedCompatibility.MuckPileType.IsInstanceOfType(blockEntity))
        {
            SendServerHudError(byEntity, "ime-message-manualmuck-no-pile");
            return true;
        }

        var processingVariant = resolvedCompatibility.GetProcessingVariant(blockEntity);
        var totalLayers = resolvedCompatibility.GetTotalLayers(blockEntity);
        if (!string.Equals(processingVariant, "coarse", StringComparison.Ordinal))
        {
            SendServerHudError(byEntity, "ime-message-manualmuck-not-coarse");
            return true;
        }

        var player = (byEntity as EntityPlayer)?.Player as IServerPlayer;
        if (totalLayers != 1 || blockLayerCount != 1)
        {
            SendServerHudError(byEntity, "ime-message-manualmuck-one-layer");
            return true;
        }

        var progress = ProgressByPile.GetOrCreateValue(blockEntity);
        var roll = progress.Strikes + 1 >= RequiredStrikes ? byEntity.World.Rand.NextDouble() : 0;
        var outcome = AdvanceProgress(progress, roll);

        if (outcome is ManualMuckStrikeOutcome.Success or ManualMuckStrikeOutcome.Failure)
        {
            bool resolved;
            try
            {
                resolved = outcome == ManualMuckStrikeOutcome.Success
                    ? resolvedCompatibility.ConvertCompositionToFine(blockEntity)
                    : resolvedCompatibility.RemoveOneLayer(blockEntity);
            }
            catch (Exception exception)
            {
                progress.Strikes = RequiredStrikes - 1;
                DisableAfterRuntimeFailure($"the muck conversion API threw {exception.GetType().Name}");
                SendServerHudError(byEntity, "ime-message-manualmuck-runtime-failure");
                return true;
            }

            if (!resolved)
            {
                progress.Strikes = RequiredStrikes - 1;
                DisableAfterRuntimeFailure("the fixture-verified muck conversion API rejected a resolution");
                SendServerHudError(byEntity, "ime-message-manualmuck-runtime-failure");
                return true;
            }

            ClearProgress(blockEntity);
        }

        byEntity.StartAnimation("hammerhit");
        byEntity.World.PlaySoundAt(
            new AssetLocation("survival:sounds/effect/anvilhit1"),
            byEntity,
            player,
            randomizePitch: true,
            range: 16f,
            volume: 0.7f);
        slot.Itemstack.Collectible.DamageItem(byEntity.World, byEntity, slot, 1, destroyOnZeroDurability: true);
        ((BlockEntity)blockEntity).MarkDirty(redrawOnClient: true, skipPlayer: player);

        if (outcome == ManualMuckStrikeOutcome.Progress)
        {
            SendServerHudProgress(
                byEntity,
                "ime-message-manualmuck-progress",
                new object[] { progress.Strikes, RequiredStrikes });
        }
        else
        {
            SendServerHudProgress(
                byEntity,
                outcome == ManualMuckStrikeOutcome.Success
                    ? "ime-message-manualmuck-success"
                    : "ime-message-manualmuck-failure",
                Array.Empty<object>());
        }

        return true;
    }

    public override WorldInteraction[] GetHeldInteractionHelp(ItemSlot inSlot, ref EnumHandling handling)
    {
        if (inSlot.Itemstack?.Collectible?.Code is not AssetLocation code || !IsEligibleHammerCode(code))
            return Array.Empty<WorldInteraction>();

        return new[]
        {
            new WorldInteraction
            {
                ActionLangCode = "ime-manualmuck-help",
                MouseButton = EnumMouseButton.Left
            }
        };
    }

    internal static bool IsEligibleHammerCode(AssetLocation? code) =>
        code is not null &&
        string.Equals(code.Domain, "game", StringComparison.Ordinal) &&
        EligibleHammerPaths.Contains(code.Path);

    internal static bool IsMuckPileBlockCode(AssetLocation? code) =>
        code is not null &&
        string.Equals(code.Domain, "interestingme", StringComparison.Ordinal) &&
        (string.Equals(code.Path, "muckpile", StringComparison.Ordinal) ||
         code.Path.StartsWith("muckpile-", StringComparison.Ordinal));

    internal static ManualMuckStrikeOutcome AdvanceProgress(ManualMuckCrusherProgress progress, double roll)
    {
        progress.Strikes++;
        if (progress.Strikes < RequiredStrikes) return ManualMuckStrikeOutcome.Progress;
        return roll < 0.5 ? ManualMuckStrikeOutcome.Success : ManualMuckStrikeOutcome.Failure;
    }

    internal static int GetProgress(object pile) =>
        ProgressByPile.TryGetValue(pile, out var progress) ? progress.Strikes : 0;

    internal static void SetProgressForCheck(object pile, int strikes)
    {
        ClearProgress(pile);
        if (strikes is > 0 and < RequiredStrikes)
            ProgressByPile.Add(pile, new ManualMuckCrusherProgress { Strikes = strikes });
    }

    internal static void ClearProgress(object pile)
    {
        ProgressByPile.Remove(pile);
    }

    internal static bool IsCoarseSingleLayerBlock(
        bool sneaking,
        string? domain,
        string? path,
        string? processingVariant,
        int layerCount) =>
        sneaking &&
        string.Equals(domain, "interestingme", StringComparison.Ordinal) &&
        string.Equals(path, "muckpile", StringComparison.Ordinal) &&
        string.Equals(processingVariant, "coarse", StringComparison.Ordinal) &&
        layerCount == 1;

    internal static bool TryInitialize(Harmony patchHarmony, ICoreAPI coreApi)
    {
        Disable();
        api = coreApi;

        if (!ManualMuckCrusherCompatibility.TryResolve(out var resolved, out var reason))
        {
            coreApi.Logger.Warning(
                "[{0}] Manual muck crushing disabled: {1}",
                HarmonyId,
                reason);
            api = null;
            return false;
        }

        var resolvedCompatibility = resolved!;
        try
        {
            var hammerType = AccessTools.TypeByName(HammerTargetTypeName);
            var heldAttack = hammerType?.GetMethod(
                "OnHeldAttackStart",
                BindingFlags.Instance | BindingFlags.Public,
                binder: null,
                types: new[]
                {
                    typeof(ItemSlot), typeof(EntityAgent), typeof(BlockSelection),
                    typeof(EntitySelection), typeof(EnumHandHandling).MakeByRefType()
                },
                modifiers: null);
            if (heldAttack is null || heldAttack.DeclaringType != hammerType || heldAttack.ReturnType != typeof(void))
                throw new MissingMethodException($"{HammerTargetTypeName}.OnHeldAttackStart(ItemSlot, EntityAgent, BlockSelection, EntitySelection, ref EnumHandHandling)");

            patchHarmony.Patch(
                heldAttack,
                prefix: new HarmonyMethod(typeof(ManualMuckCrusher), nameof(HeldAttackPrefix)));
            PatchedSerializationTargets.Add(heldAttack);
            patchHarmony.Patch(
                resolvedCompatibility.ToTreeAttributes,
                postfix: new HarmonyMethod(typeof(ManualMuckCrusher), nameof(SaveProgress)));
            PatchedSerializationTargets.Add(resolvedCompatibility.ToTreeAttributes);
            patchHarmony.Patch(
                resolvedCompatibility.FromTreeAttributes,
                postfix: new HarmonyMethod(typeof(ManualMuckCrusher), nameof(LoadProgress)));
            PatchedSerializationTargets.Add(resolvedCompatibility.FromTreeAttributes);
            patchHarmony.Patch(
                resolvedCompatibility.OnBlockRemoved,
                prefix: new HarmonyMethod(typeof(ManualMuckCrusher), nameof(ClearRemovedProgress)));
            PatchedSerializationTargets.Add(resolvedCompatibility.OnBlockRemoved);
        }
        catch (Exception exception)
        {
            foreach (var target in PatchedSerializationTargets)
                patchHarmony.Unpatch(target, HarmonyPatchType.All, HarmonyId);
            PatchedSerializationTargets.Clear();
            coreApi.Logger.Warning(
                "[{0}] Manual muck crushing disabled: failed to patch the held-attack hook or exact muck-pile serialization lifecycle ({1}).",
                HarmonyId,
                exception.Message);
            api = null;
            return false;
        }

        compatibility = resolved;
        harmony = patchHarmony;
        coreApi.Logger.Notification(
            "[{0}] Manual muck crushing enabled for selected vanilla hammers (sneak + left-click).",
            HarmonyId);
        return true;
    }

    internal static void Disable()
    {
        if (harmony is not null)
        {
            foreach (var target in PatchedSerializationTargets)
                harmony.Unpatch(target, HarmonyPatchType.All, HarmonyId);
        }

        PatchedSerializationTargets.Clear();
        harmony = null;
        compatibility = null;
        api = null;
    }

    private static void SaveProgress(object __instance, ITreeAttribute tree)
    {
        if (ProgressByPile.TryGetValue(__instance, out var progress) && progress.Strikes is > 0 and < RequiredStrikes)
            tree.SetInt(ProgressTreeKey, progress.Strikes);
        else
            tree.RemoveAttribute(ProgressTreeKey);
    }

    private static void LoadProgress(object __instance, ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        var strikes = tree.GetInt(ProgressTreeKey, 0);
        ClearProgress(__instance);
        if (strikes is > 0 and < RequiredStrikes)
            ProgressByPile.Add(__instance, new ManualMuckCrusherProgress { Strikes = strikes });
    }

    private static void ClearRemovedProgress(object __instance)
    {
        ClearProgress(__instance);
    }

    private static void PreventDefault(ref EnumHandHandling handHandling, ref EnumHandling handling)
    {
        handHandling = EnumHandHandling.PreventDefault;
        handling = EnumHandling.PreventDefault;
    }

    private static void SendServerHudError(EntityAgent byEntity, string messageKey)
    {
        if (byEntity.World.Side == EnumAppSide.Server)
        {
            var player = (byEntity as EntityPlayer)?.Player as IServerPlayer;
            if (player is null) return;

            player.SendIngameError(messageKey, GetHudText(messageKey, Array.Empty<object>()), Array.Empty<object>());
        }
    }

    private static void SendServerHudProgress(EntityAgent byEntity, string messageKey, object[] arguments)
    {
        if (byEntity.World.Side == EnumAppSide.Server)
        {
            var player = (byEntity as EntityPlayer)?.Player as IServerPlayer;
            if (player is null) return;

            player.SendIngameError(messageKey, GetHudText(messageKey, arguments), Array.Empty<object>());
        }
    }

    private static string GetHudText(string messageKey, object[] arguments)
    {
        var translated = Lang.Get(messageKey, arguments);
        if (!string.Equals(translated, messageKey, StringComparison.Ordinal) &&
            !translated.StartsWith("ime-message-manualmuck-", StringComparison.Ordinal))
            return translated;

        return messageKey switch
        {
            "ime-message-manualmuck-disabled" => "Manual muck crushing is unavailable because the required InterestingME data could not be read.",
            "ime-message-manualmuck-wrong-hammer" => "This hammer cannot crush muck. Use a tin-bronze, bismuth-bronze, black-bronze, iron, meteoric-iron, or steel hammer.",
            "ime-message-manualmuck-not-coarse" => "Only coarse muck can be crushed manually; raw and fine muck cannot.",
            "ime-message-manualmuck-one-layer" => "Manual crushing requires exactly one coarse muck layer.",
            "ime-message-manualmuck-no-pile" => "I could not read that muck pile. Aim at the pile and try again.",
            "ime-message-manualmuck-progress" when arguments.Length >= 2 => $"Muck strike {arguments[0]}/{arguments[1]}.",
            "ime-message-manualmuck-success" => "The coarse muck became fine muck.",
            "ime-message-manualmuck-failure" => "The coarse muck was destroyed.",
            "ime-message-manualmuck-runtime-failure" => "The muck could not be crushed because its data was invalid. Please try again later.",
            _ => messageKey
        };
    }

    private static void DisableAfterRuntimeFailure(string reason)
    {
        var currentApi = api;
        Disable();
        currentApi?.Logger.Warning("[{0}] Manual muck crushing disabled after a runtime compatibility failure: {1}.", HarmonyId, reason);
    }
}

internal sealed class ManualMuckCrusherProgress
{
    internal int Strikes { get; set; }
}

internal enum ManualMuckStrikeOutcome
{
    Progress,
    Success,
    Failure
}

internal sealed class ManualMuckCrusherCompatibility
{
    internal required Type MuckPileType { get; init; }
    internal required Type MuckBlockType { get; init; }
    internal required MethodInfo ToTreeAttributes { get; init; }
    internal required MethodInfo FromTreeAttributes { get; init; }
    internal required MethodInfo OnBlockRemoved { get; init; }
    internal required MethodInfo SetCompositionDirect { get; init; }
    internal required MethodInfo TryExtractLayers { get; init; }
    internal required PropertyInfo CompositionProperty { get; init; }
    internal required PropertyInfo ProcessingVariantProperty { get; init; }
    internal required PropertyInfo TotalLayersProperty { get; init; }
    internal required PropertyInfo CompositionEntriesProperty { get; init; }
    internal required PropertyInfo EntryProcessingVariantProperty { get; init; }
    internal required MethodInfo BlockGetProcessingVariant { get; init; }
    internal required MethodInfo BlockGetLayerCount { get; init; }
    internal required MethodInfo CompositionToTreeAttribute { get; init; }
    internal required MethodInfo CompositionFromTreeAttribute { get; init; }

    internal string GetProcessingVariant(object pile) => (string)ProcessingVariantProperty.GetValue(pile)!;

    internal int GetTotalLayers(object pile) => (int)TotalLayersProperty.GetValue(pile)!;

    internal string GetBlockProcessingVariant(Block block) => (string)BlockGetProcessingVariant.Invoke(block, null)!;

    internal int GetBlockLayerCount(Block block) => (int)BlockGetLayerCount.Invoke(block, null)!;

    internal bool ConvertCompositionToFine(object pile)
    {
        var composition = CompositionProperty.GetValue(pile);
        if (composition is null) return false;

        if (!SetCompositionEntriesToFine(composition)) return false;

        SetCompositionDirect.Invoke(pile, new[] { composition, "fine", true, true });
        return true;
    }

    internal bool SetCompositionEntriesToFine(object composition)
    {
        if (CompositionEntriesProperty.GetValue(composition) is not IEnumerable entries) return false;
        foreach (var entry in entries)
        {
            if (entry is null) return false;
            EntryProcessingVariantProperty.SetValue(entry, "fine");
        }

        return true;
    }

    internal bool RemoveOneLayer(object pile)
    {
        var arguments = new object?[] { 1, null };
        return (bool)TryExtractLayers.Invoke(pile, arguments)!;
    }

    internal static bool TryResolve(out ManualMuckCrusherCompatibility? result, out string reason)
    {
        result = null;
        var muckPileType = AccessTools.TypeByName("IME.BlockEntityMuckPile");
        var muckCompositionType = AccessTools.TypeByName("IME.MuckComposition");
        var muckBlockType = AccessTools.TypeByName("IME.BlockMuckPile");
        if (muckPileType is null || muckCompositionType is null || muckBlockType is null)
        {
            reason = "IME.BlockEntityMuckPile, IME.MuckComposition, or IME.BlockMuckPile was not found";
            return false;
        }

        var treeType = typeof(ITreeAttribute);
        var worldType = typeof(IWorldAccessor);
        var compositionByRef = muckCompositionType.MakeByRefType();
        var toTree = ExactMethod(muckPileType, "ToTreeAttributes", typeof(void), BindingFlags.Instance | BindingFlags.Public, treeType);
        var fromTree = ExactMethod(muckPileType, "FromTreeAttributes", typeof(void), BindingFlags.Instance | BindingFlags.Public, treeType, worldType);
        var removed = ExactMethod(muckPileType, "OnBlockRemoved", typeof(void), BindingFlags.Instance | BindingFlags.Public);
        var setComposition = ExactMethod(muckPileType, "SetCompositionDirect", typeof(void), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, muckCompositionType, typeof(string), typeof(bool), typeof(bool));
        var extractLayers = ExactMethod(muckPileType, typeof(bool), "TryExtractLayers", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, typeof(int), compositionByRef);
        var compositionProperty = ExactProperty(muckPileType, "Composition", muckCompositionType);
        var processingProperty = ExactProperty(muckPileType, "ProcessingVariant", typeof(string));
        var totalLayersProperty = ExactProperty(muckPileType, "TotalLayers", typeof(int));
        var compositionEntriesProperty = muckCompositionType.GetProperty("Entries", BindingFlags.Instance | BindingFlags.Public);
        var entryType = compositionEntriesProperty?.PropertyType.GetGenericArguments().SingleOrDefault();
        var entryProcessingProperty = entryType?.GetProperty("ProcessingVariant", BindingFlags.Instance | BindingFlags.Public);
        var getBlockProcessing = ExactMethod(muckBlockType, "GetProcessingVariant", typeof(string), BindingFlags.Instance | BindingFlags.Public);
        var getBlockLayers = ExactMethod(muckBlockType, "GetLayerCount", typeof(int), BindingFlags.Instance | BindingFlags.Public);
        var compositionToTree = ExactMethod(muckCompositionType, "ToTreeAttribute", typeof(void), BindingFlags.Instance | BindingFlags.Public, treeType);
        var compositionFromTree = ExactMethod(muckCompositionType, "FromTreeAttribute", muckCompositionType, BindingFlags.Static | BindingFlags.Public, treeType);

        if (toTree is null || fromTree is null || removed is null || setComposition is null || extractLayers is null ||
            compositionProperty is null || processingProperty is null || totalLayersProperty is null ||
            compositionEntriesProperty is null || entryProcessingProperty is null || !entryProcessingProperty.CanWrite || getBlockProcessing is null ||
            getBlockLayers is null || compositionToTree is null || compositionFromTree is null)
        {
            reason = "the exact fixture-verified muck conversion or serialization signature changed";
            return false;
        }

        result = new ManualMuckCrusherCompatibility
        {
            MuckPileType = muckPileType,
            MuckBlockType = muckBlockType,
            ToTreeAttributes = toTree,
            FromTreeAttributes = fromTree,
            OnBlockRemoved = removed,
            SetCompositionDirect = setComposition,
            TryExtractLayers = extractLayers,
            CompositionProperty = compositionProperty,
            ProcessingVariantProperty = processingProperty,
            TotalLayersProperty = totalLayersProperty,
            CompositionEntriesProperty = compositionEntriesProperty,
            EntryProcessingVariantProperty = entryProcessingProperty,
            BlockGetProcessingVariant = getBlockProcessing,
            BlockGetLayerCount = getBlockLayers,
            CompositionToTreeAttribute = compositionToTree,
            CompositionFromTreeAttribute = compositionFromTree
        };
        reason = string.Empty;
        return true;
    }

    private static MethodInfo? ExactMethod(Type type, string name, Type returnType, BindingFlags flags, params Type[] parameters) =>
        ExactMethod(type, returnType, name, flags, parameters);

    private static MethodInfo? ExactMethod(Type type, Type returnType, string name, BindingFlags flags, params Type[] parameters)
    {
        var method = type.GetMethod(name, flags, binder: null, types: parameters, modifiers: null);
        return method is not null && method.ReturnType == returnType ? method : null;
    }

    private static PropertyInfo? ExactProperty(Type type, string name, Type propertyType)
    {
        var property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return property?.PropertyType == propertyType && property.GetMethod is not null ? property : null;
    }
}
