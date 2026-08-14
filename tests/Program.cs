using System.Collections;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Vintagestory.API.Common;

var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
var configuration = Environment.GetEnvironmentVariable("Configuration") ?? "Release";
var gamePath = Environment.GetEnvironmentVariable("VintageStoryPath")
    ?? Environment.GetEnvironmentVariable("VINTAGE_STORY_PATH")
    ?? throw new InvalidOperationException("Set VintageStoryPath or VINTAGE_STORY_PATH to run the integration checks.");
var modPath = Path.Combine(
    repositoryRoot,
    "src",
    "bin",
    configuration,
    "net10.0",
    "InterestingMeMaterialNeedsFurnacePatch.dll");
var interestingMePath = Path.Combine(repositoryRoot, "references", "interestingme-v1.0.16", "interestingme.dll");
var survivalModPath = Path.Combine(gamePath, "Mods", "VSSurvivalMod.dll");

if (!File.Exists(modPath)) throw new FileNotFoundException("Build the mod before running tests.", modPath);
if (!File.Exists(interestingMePath)) throw new FileNotFoundException("InterestingME reference is missing.", interestingMePath);
if (!File.Exists(survivalModPath)) throw new FileNotFoundException("Vintage Story survival assembly is missing.", survivalModPath);

AssemblyLoadContext.Default.Resolving += (_, name) =>
{
    foreach (var directory in new[] { gamePath, Path.Combine(gamePath, "Lib") })
    {
        var candidate = Path.Combine(directory, name.Name + ".dll");
        if (File.Exists(candidate)) return AssemblyLoadContext.Default.LoadFromAssemblyPath(candidate);
    }

    return null;
};

var modAssembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(modPath);
var interestingAssembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(interestingMePath);
var survivalAssembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(survivalModPath);
var systemType = modAssembly.GetType("InterestingMeMaterialNeedsFurnacePatch.MaterialNeedsFurnacePatchSystem")!;
var lowTempPostfix = systemType.GetMethod("IsMaterialNeedsTierOneMudbrick", BindingFlags.Static | BindingFlags.NonPublic)!;
var glazedLowTempPostfix = systemType.GetMethod("IsBricklayersTierTwoGlazedBrick", BindingFlags.Static | BindingFlags.NonPublic)!;
var roastingPostfix = systemType.GetMethod("IsRoastingGlazedBrick", BindingFlags.Static | BindingFlags.NonPublic)!;
var manualCrusherType = modAssembly.GetType("InterestingMeMaterialNeedsFurnacePatch.ManualMuckCrusher")!;
var manualAttackStart = manualCrusherType.GetMethod("OnHeldAttackStart", BindingFlags.Instance | BindingFlags.Public)!;
Assert(manualAttackStart.ReturnType == typeof(void), "Manual muck crushing must use the collectible attack-start callback.");
var hammerTargetTypeName = (string)manualCrusherType
    .GetField("HammerTargetTypeName", BindingFlags.Static | BindingFlags.NonPublic)!
    .GetRawConstantValue()!;
var hammerTargetType = survivalAssembly.GetType(hammerTargetTypeName)!;
var hammerHeldAttackStart = hammerTargetType.GetMethod(
    "OnHeldAttackStart",
    BindingFlags.Instance | BindingFlags.Public,
    binder: null,
    types: new[]
    {
        typeof(ItemSlot), typeof(EntityAgent), typeof(BlockSelection),
        typeof(EntitySelection), typeof(EnumHandHandling).MakeByRefType()
    },
    modifiers: null);
Assert(
    hammerHeldAttackStart is not null &&
    hammerHeldAttackStart.DeclaringType == hammerTargetType &&
    hammerHeldAttackStart.ReturnType == typeof(void),
    "Vintage Story ItemHammer held-attack override changed; manual crushing cannot be hooked safely.");
var manualHeldAttackPrefix = manualCrusherType.GetMethod("HeldAttackPrefix", BindingFlags.Static | BindingFlags.NonPublic);
Assert(manualHeldAttackPrefix is not null && manualHeldAttackPrefix.ReturnType == typeof(bool), "Manual crushing must provide a direct held-attack Harmony prefix.");
var targetAttackParameters = hammerHeldAttackStart!.GetParameters();
var prefixAttackParameters = manualHeldAttackPrefix!.GetParameters();
Assert(
    prefixAttackParameters.Length == targetAttackParameters.Length &&
    prefixAttackParameters.Select(parameter => parameter.Name).SequenceEqual(targetAttackParameters.Select(parameter => parameter.Name)) &&
    prefixAttackParameters.Select(parameter => parameter.ParameterType).SequenceEqual(targetAttackParameters.Select(parameter => parameter.ParameterType)),
    "The manual held-attack prefix parameter names and types must exactly match Vintage Story for Harmony binding.");
var manualBehaviorName = (string)manualCrusherType
    .GetField("BehaviorName", BindingFlags.Static | BindingFlags.NonPublic)!
    .GetRawConstantValue()!;
var manualHammerCodeCheck = manualCrusherType.GetMethod("IsEligibleHammerCode", BindingFlags.Static | BindingFlags.NonPublic)!;
var manualMuckBlockCodeCheck = manualCrusherType.GetMethod("IsMuckPileBlockCode", BindingFlags.Static | BindingFlags.NonPublic)!;
var manualPileGate = manualCrusherType.GetMethod("IsCoarseSingleLayerBlock", BindingFlags.Static | BindingFlags.NonPublic)!;
var manualProgressType = modAssembly.GetType("InterestingMeMaterialNeedsFurnacePatch.ManualMuckCrusherProgress")!;
var advanceManualStrike = manualCrusherType.GetMethod("AdvanceProgress", BindingFlags.Static | BindingFlags.NonPublic)!;
var setManualProgress = manualCrusherType.GetMethod("SetProgressForCheck", BindingFlags.Static | BindingFlags.NonPublic)!;
var getManualProgress = manualCrusherType.GetMethod("GetProgress", BindingFlags.Static | BindingFlags.NonPublic)!;
var clearManualProgress = manualCrusherType.GetMethod("ClearProgress", BindingFlags.Static | BindingFlags.NonPublic)!;
var saveManualProgress = manualCrusherType.GetMethod("SaveProgress", BindingFlags.Static | BindingFlags.NonPublic)!;
var loadManualProgress = manualCrusherType.GetMethod("LoadProgress", BindingFlags.Static | BindingFlags.NonPublic)!;
var clearRemovedManualProgress = manualCrusherType.GetMethod("ClearRemovedProgress", BindingFlags.Static | BindingFlags.NonPublic)!;

bool ApplyPostfix(int tier, string? path, bool originalResult = false)
{
    var arguments = new object?[] { tier, path, originalResult };
    lowTempPostfix.Invoke(null, arguments);
    glazedLowTempPostfix.Invoke(null, arguments);
    return (bool)arguments[2]!;
}

bool ApplyRoastingPostfix(string? path, bool originalResult = false)
{
    var arguments = new object?[] { path, originalResult };
    roastingPostfix.Invoke(null, arguments);
    return (bool)arguments[1]!;
}

void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

bool IsEligibleHammer(string code) =>
    (bool)manualHammerCodeCheck.Invoke(null, new object?[] { new AssetLocation(code) })!;

bool IsMuckPileBlockCode(string code) =>
    (bool)manualMuckBlockCodeCheck.Invoke(null, new object?[] { new AssetLocation(code) })!;

bool IsCoarseSingleLayer(bool sneaking, string domain, string path, string processing, int layerCount) =>
    (bool)manualPileGate.Invoke(null, new object?[] { sneaking, domain, path, processing, layerCount })!;

object NewManualProgress() => Activator.CreateInstance(manualProgressType)!;

string AdvanceManualStrike(object progress, double roll) =>
    advanceManualStrike.Invoke(null, new[] { progress, (object)roll })!.ToString()!;

var materialNeedsPaths = new[]
{
    "brownmudbrick-light", "brownmudbrick-dark",
    "graymudbrick-light", "graymudbrick-dark",
    "oxbloodmudbrick-light", "oxbloodmudbrick-dark",
    "oystermudbrick-light", "oystermudbrick-dark",
    "redmudbrick-light", "redmudbrick-dark"
};

var vanillaPaths = new[] { "mudbrick-light", "mudbrick-dark" };
var glazedBrickColors = new[]
{
    "black", "blue", "brown", "gold", "green", "greenblue", "lapislazuli", "malachite",
    "orange", "pink", "purple", "red", "redbrown", "white", "yellow"
};
var glazedBrickPaths = new[] { "clear", "milky" }
    .SelectMany(glaze => glazedBrickColors.Select(color => $"glazedbricks-{glaze}-{color}"))
    .ToArray();
var bricklayersPaths = new[]
{
    "mudbricks-ash", "mudbricks-blue", "mudbricks-brown", "mudbricks-browngolden",
    "mudbricks-brownlight", "mudbricks-brownweathered", "mudbricks-green", "mudbricks-orange",
    "mudbricks-pink", "mudbricks-tan", "mudbricks-yellow"
};

var recipeCodes = new[]
{
    "game:mudbrick-light", "game:mudbrick-dark",
    "materialneeds:brownmudbrick-light", "materialneeds:brownmudbrick-dark",
    "materialneeds:graymudbrick-light", "materialneeds:graymudbrick-dark",
    "materialneeds:oxbloodmudbrick-light", "materialneeds:oxbloodmudbrick-dark",
    "materialneeds:oystermudbrick-light", "materialneeds:oystermudbrick-dark",
    "materialneeds:redmudbrick-light", "materialneeds:redmudbrick-dark",
    "bricklayers:mudbricks-ash", "bricklayers:mudbricks-blue", "bricklayers:mudbricks-brown",
    "bricklayers:mudbricks-browngolden", "bricklayers:mudbricks-brownlight",
    "bricklayers:mudbricks-brownweathered", "bricklayers:mudbricks-green",
    "bricklayers:mudbricks-orange", "bricklayers:mudbricks-pink", "bricklayers:mudbricks-tan",
    "bricklayers:mudbricks-yellow"
};

var recipePath = Path.Combine(
    repositoryRoot,
    "assets",
    "ime-olendril-patch",
    "recipes",
    "grid",
    "blocks",
    "lowtempfurnacedoor.json");
Assert(File.Exists(recipePath), "The mod-owned low-temperature furnace door recipe asset is missing.");
var screenRecipePath = Path.Combine(
    repositoryRoot,
    "assets",
    "ime-olendril-patch",
    "recipes",
    "grid",
    "blocks",
    "screen.json");
Assert(File.Exists(screenRecipePath), "The mod-owned Screen recipe asset is missing.");
var upstreamScreenRecipePath = Path.Combine(
    repositoryRoot,
    "references",
    "interestingme-v1.0.16",
    "assets",
    "interestingme",
    "recipes",
    "grid",
    "blocks",
    "screen.json");
Assert(File.Exists(upstreamScreenRecipePath), "The InterestingME 1.0.16 Screen recipe fixture is missing.");
var bricklayersRecipePath = Path.Combine(
    repositoryRoot, "references", "bricklayers-3.2.2", "assets", "bricklayers", "recipes", "grid", "bricks", "glazedclaybricks.json");
Assert(File.Exists(bricklayersRecipePath), "The Bricklayers 3.2.2 glazed-brick fixture is missing.");
using (var bricklayers = JsonDocument.Parse(File.ReadAllText(bricklayersRecipePath)))
{
    var definitions = bricklayers.RootElement.EnumerateArray().ToArray();
    Assert(definitions.Length == 2, "Bricklayers must define exactly clear and milky glazed-brick variants.");
    var fixturePaths = definitions.SelectMany(definition =>
    {
        var output = definition.GetProperty("output").GetProperty("code").GetString()!;
        var glaze = output.Split('-')[1];
        return definition.GetProperty("ingredients").GetProperty("B").GetProperty("allowedVariants")
            .EnumerateArray().Select(color => $"glazedbricks-{glaze}-{color.GetString()}");
    }).ToHashSet(StringComparer.Ordinal);
    Assert(fixturePaths.SetEquals(glazedBrickPaths), "The glazed-brick allowlist must exactly match the Bricklayers 3.2.2 fixture.");
}
using (var recipes = JsonDocument.Parse(File.ReadAllText(recipePath)))
{
    var recipeElements = recipes.RootElement;
    Assert(recipeElements.ValueKind == JsonValueKind.Array, "The door recipe asset must contain an explicit recipe array.");
    Assert(recipeElements.GetArrayLength() == recipeCodes.Length, "The door recipe asset must contain all 23 full mudbrick recipes.");

    var actualCodes = new List<string>();
    foreach (var recipe in recipeElements.EnumerateArray())
    {
        Assert(recipe.GetProperty("ingredientPattern").GetString() == "CCC,CCC,C_C", "Every door recipe must preserve the 3x3 shape.");
        Assert(recipe.GetProperty("width").GetInt32() == 3, "Every door recipe must have width 3.");
        Assert(recipe.GetProperty("height").GetInt32() == 3, "Every door recipe must have height 3.");

        var ingredients = recipe.GetProperty("ingredients");
        Assert(ingredients.EnumerateObject().Count() == 1, "Every door recipe must have exactly one ingredient definition.");
        var ingredient = ingredients.GetProperty("C");
        Assert(ingredient.GetProperty("type").GetString() == "block", "Every door ingredient must be a block.");
        Assert(ingredient.GetProperty("quantity").GetInt32() == 2, "Every door recipe must use two blocks per C ingredient.");
        actualCodes.Add(ingredient.GetProperty("code").GetString()!);

        var output = recipe.GetProperty("output");
        Assert(output.GetProperty("type").GetString() == "block", "Every door recipe must output a block.");
        Assert(output.GetProperty("code").GetString() == "interestingme:lowtempfurnacedoor-closed-north", "Every door recipe must output the InterestingME smeltery door.");
        Assert(output.GetProperty("quantity").GetInt32() == 1, "Every door recipe must output one door.");
    }

    Assert(actualCodes.SequenceEqual(recipeCodes), "The recipe allowlist must contain exactly the 23 expected full mudbrick paths.");
    Assert(actualCodes.All(code => !code.Contains('*')), "Door recipes must not use wildcard ingredient paths.");
    Assert(actualCodes.All(code => !code.Contains("slab") && !code.Contains("stairs") && !code.Contains("wall")), "Door recipes must exclude non-full mudbrick shapes.");
    Assert(!actualCodes.Contains("materialneeds:mudbrick-*"), "Door recipes must exclude arbitrary mudbrick wildcard paths.");
}
using (var screenRecipes = JsonDocument.Parse(File.ReadAllText(screenRecipePath)))
{
    var definitions = screenRecipes.RootElement.EnumerateArray().ToArray();
    Assert(definitions.Length == 2, "The Screen asset must contain exactly two explicit bronze recipes.");

    var expectedRodCodes = new[] { "game:rod-blackbronze", "game:rod-bismuthbronze" };
    var actualRodCodes = new List<string>();
    foreach (var recipe in definitions)
    {
        Assert(recipe.GetProperty("ingredientPattern").GetString() == "SMS,PPP,PPS", "Screen recipes must preserve the upstream pattern.");
        Assert(recipe.GetProperty("width").GetInt32() == 3, "Screen recipes must have width 3.");
        Assert(recipe.GetProperty("height").GetInt32() == 3, "Screen recipes must have height 3.");

        var ingredients = recipe.GetProperty("ingredients");
        Assert(ingredients.EnumerateObject().Count() == 3, "Screen recipes must have support beam, plank, and rod ingredients.");
        Assert(ingredients.GetProperty("S").GetProperty("type").GetString() == "block", "Screen support beams must be blocks.");
        Assert(ingredients.GetProperty("S").GetProperty("code").GetString() == "game:supportbeam-*", "Screen recipes must preserve wildcard support beams.");
        Assert(ingredients.GetProperty("S").GetProperty("quantity").GetInt32() == 2, "Screen recipes must use two support beams.");
        Assert(ingredients.GetProperty("P").GetProperty("type").GetString() == "item", "Screen planks must be items.");
        Assert(ingredients.GetProperty("P").GetProperty("code").GetString() == "game:plank-*", "Screen recipes must preserve wildcard planks.");
        Assert(ingredients.GetProperty("P").GetProperty("quantity").GetInt32() == 4, "Screen recipes must use four planks.");

        var rod = ingredients.GetProperty("M");
        Assert(rod.GetProperty("type").GetString() == "item", "Screen rods must be items.");
        Assert(rod.GetProperty("quantity").GetInt32() == 7, "Screen recipes must use seven rods.");
        var rodCode = rod.GetProperty("code").GetString()!;
        actualRodCodes.Add(rodCode);
        Assert(!rodCode.Contains('*'), "Screen rod recipes must not use wildcard paths.");
        Assert(rodCode is "game:rod-blackbronze" or "game:rod-bismuthbronze", "Only the two requested bronze rod paths may be added.");

        var output = recipe.GetProperty("output");
        Assert(output.GetProperty("type").GetString() == "block", "Screen recipes must output a block.");
        Assert(output.GetProperty("code").GetString() == "interestingme:screen-north", "Screen recipes must output the upstream Screen.");
        Assert(output.GetProperty("quantity").GetInt32() == 1, "Screen recipes must output one Screen.");
    }

    Assert(actualRodCodes.SequenceEqual(expectedRodCodes), "Screen recipes must contain exactly black-bronze and bismuth-bronze rods.");
    Assert(actualRodCodes.All(code => !code.Contains("iron") && !code.Contains("steel") && code.EndsWith("bronze")), "Screen recipes must exclude iron, steel, and unrelated rod paths.");
}
var upstreamScreen = File.ReadAllText(upstreamScreenRecipePath);
Assert(upstreamScreen.Contains("ingredientPattern: \"SMS,PPP,PPS\""), "The upstream Screen pattern must remain unchanged.");
Assert(upstreamScreen.Contains("code: \"game:rod-tinbronze\", quantity: 7"), "The upstream tin-bronze Screen recipe must remain untouched.");
Assert(!upstreamScreen.Contains("rod-blackbronze") && !upstreamScreen.Contains("rod-bismuthbronze"), "The upstream Screen fixture must remain tin-bronze-only.");

var eligibleHammerPaths = new[]
{
    "hammer-tinbronze", "hammer-bismuthbronze", "hammer-blackbronze",
    "hammer-iron", "hammer-meteoriciron", "hammer-steel"
};
foreach (var path in eligibleHammerPaths)
    Assert(IsEligibleHammer($"game:{path}"), $"Vanilla hammer {path} must be eligible for manual crushing.");
foreach (var path in new[]
{
    "hammer-copper", "hammer-gold", "hammer-silver", "hammer-stone", "hammer-*",
    "hammer-tinbronze-head", "hammer-tinbronze-mod"
})
    Assert(!IsEligibleHammer($"game:{path}"), $"Hammer {path} must be excluded from manual crushing.");
Assert(!IsEligibleHammer("mod:hammer-steel"), "A namespaced mod hammer must not match the vanilla allowlist.");
Assert(IsMuckPileBlockCode("interestingme:muckpile-coarse-ore-none-granite-1"), "A variant muck-pile block must be recognized for feedback.");
Assert(!IsMuckPileBlockCode("game:dirt"), "Non-muck blocks must not be recognized as muck-pile feedback targets.");
Assert(IsCoarseSingleLayer(true, "interestingme", "muckpile", "coarse", 1), "Sneaking at a single coarse muck layer must pass the manual gate.");
Assert(!IsCoarseSingleLayer(false, "interestingme", "muckpile", "coarse", 1), "Manual crushing must require sneaking.");
Assert(!IsCoarseSingleLayer(true, "interestingme", "muckpile", "fine", 1), "Fine muck must not pass the manual gate.");
Assert(!IsCoarseSingleLayer(true, "interestingme", "muckpile", "raw", 1), "Raw muck must not pass the manual gate.");
Assert(!IsCoarseSingleLayer(true, "interestingme", "muckpile", "coarse", 2), "A multi-layer muck pile must not pass the manual gate.");
Assert(!IsCoarseSingleLayer(true, "modded", "muckpile", "coarse", 1), "A modded muck block must not pass the manual gate.");

var manualPatchPath = Path.Combine(
    repositoryRoot,
    "assets",
    "ime-olendril-patch",
    "patches",
    "itemtypes",
    "tool",
    "hammer.json");
Assert(File.Exists(manualPatchPath), "The vanilla hammer behavior patch is missing.");
using (var manualPatch = JsonDocument.Parse(File.ReadAllText(manualPatchPath)))
{
    var operations = manualPatch.RootElement.EnumerateArray().ToArray();
    Assert(operations.Length == 1, "The manual crusher must use one hammer behavior patch operation.");
    var behaviorOperation = operations[0];
    Assert(behaviorOperation.GetProperty("file").GetString() == "game:itemtypes/tool/hammer.json", "The manual crusher must target the vanilla hammer definition.");
    Assert(behaviorOperation.GetProperty("op").GetString() == "add", "The manual crusher patch must append behavior rather than replace the definition.");
    Assert(behaviorOperation.GetProperty("path").GetString() == "/behaviors/-", "The manual crusher must append to vanilla hammer behaviors.");
    Assert(behaviorOperation.GetProperty("value").GetProperty("name").GetString() == manualBehaviorName, "The hammer patch must use the registered manual crusher behavior name.");
}

var languagePath = Path.Combine(repositoryRoot, "assets", "ime-olendril-patch", "lang", "en.json");
using (var language = JsonDocument.Parse(File.ReadAllText(languagePath)))
{
    foreach (var key in new[]
    {
        "ime-manualmuck-help", "ime-message-manualmuck-disabled",
        "ime-message-manualmuck-wrong-hammer", "ime-message-manualmuck-not-coarse",
        "ime-message-manualmuck-one-layer", "ime-message-manualmuck-no-pile",
        "ime-message-manualmuck-progress",
        "ime-message-manualmuck-success", "ime-message-manualmuck-failure",
        "ime-message-manualmuck-runtime-failure"
    })
        Assert(language.RootElement.TryGetProperty(key, out _), $"Manual crushing localization key {key} is missing.");
    foreach (var key in new[]
    {
        "ingameerror-ime-message-manualmuck-disabled", "ingameerror-ime-message-manualmuck-wrong-hammer",
        "ingameerror-ime-message-manualmuck-not-coarse", "ingameerror-ime-message-manualmuck-one-layer",
        "ingameerror-ime-message-manualmuck-no-pile", "ingameerror-ime-message-manualmuck-progress",
        "ingameerror-ime-message-manualmuck-success", "ingameerror-ime-message-manualmuck-failure",
        "ingameerror-ime-message-manualmuck-runtime-failure"
    })
        Assert(language.RootElement.TryGetProperty(key, out _), $"Manual crushing HUD localization key {key} is missing.");
}

var muckPileType = interestingAssembly.GetType("IME.BlockEntityMuckPile")!;
var muckBlockType = interestingAssembly.GetType("IME.BlockMuckPile")!;
var muckCompositionType = interestingAssembly.GetType("IME.MuckComposition")!;
var muckEntryType = interestingAssembly.GetType("IME.MuckEntry")!;
var exactBlockProcessing = muckBlockType.GetMethod("GetProcessingVariant", BindingFlags.Instance | BindingFlags.Public, binder: null, types: Type.EmptyTypes, modifiers: null);
var exactBlockLayerCount = muckBlockType.GetMethod("GetLayerCount", BindingFlags.Instance | BindingFlags.Public, binder: null, types: Type.EmptyTypes, modifiers: null);
Assert(exactBlockProcessing is not null && exactBlockProcessing.ReturnType == typeof(string), "InterestingME muck block processing signature changed.");
Assert(exactBlockLayerCount is not null && exactBlockLayerCount.ReturnType == typeof(int), "InterestingME muck block layer signature changed.");
var exactSetComposition = muckPileType.GetMethod(
    "SetCompositionDirect",
    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
    binder: null,
    types: new[] { muckCompositionType, typeof(string), typeof(bool), typeof(bool) },
    modifiers: null);
Assert(exactSetComposition is not null && exactSetComposition.ReturnType == typeof(void), "InterestingME muck conversion signature changed.");
var exactExtractLayers = muckPileType.GetMethod(
    "TryExtractLayers",
    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
    binder: null,
    types: new[] { typeof(int), muckCompositionType.MakeByRefType() },
    modifiers: null);
Assert(exactExtractLayers is not null && exactExtractLayers.ReturnType == typeof(bool), "InterestingME muck layer extraction signature changed.");
var exactToTree = muckPileType.GetMethod(
    "ToTreeAttributes",
    BindingFlags.Instance | BindingFlags.Public,
    binder: null,
    types: new[] { typeof(Vintagestory.API.Datastructures.ITreeAttribute) },
    modifiers: null);
var exactFromTree = muckPileType.GetMethod(
    "FromTreeAttributes",
    BindingFlags.Instance | BindingFlags.Public,
    binder: null,
    types: new[] { typeof(Vintagestory.API.Datastructures.ITreeAttribute), typeof(IWorldAccessor) },
    modifiers: null);
var exactRemoved = muckPileType.GetMethod("OnBlockRemoved", BindingFlags.Instance | BindingFlags.Public, binder: null, types: Type.EmptyTypes, modifiers: null);
Assert(exactToTree is not null && exactFromTree is not null && exactRemoved is not null, "InterestingME muck serialization lifecycle signatures changed.");
var exactCompositionToTree = muckCompositionType.GetMethod(
    "ToTreeAttribute",
    BindingFlags.Instance | BindingFlags.Public,
    binder: null,
    types: new[] { typeof(Vintagestory.API.Datastructures.ITreeAttribute) },
    modifiers: null);
var exactCompositionFromTree = muckCompositionType.GetMethod(
    "FromTreeAttribute",
    BindingFlags.Static | BindingFlags.Public,
    binder: null,
    types: new[] { typeof(Vintagestory.API.Datastructures.ITreeAttribute) },
    modifiers: null);
Assert(exactCompositionToTree is not null && exactCompositionFromTree is not null, "InterestingME composition serialization signatures changed.");

var compatibilityType = modAssembly.GetType("InterestingMeMaterialNeedsFurnacePatch.ManualMuckCrusherCompatibility")!;
var resolveCompatibility = compatibilityType.GetMethod("TryResolve", BindingFlags.Static | BindingFlags.NonPublic)!;
var compatibilityArguments = new object?[] { null, null };
Assert((bool)resolveCompatibility.Invoke(null, compatibilityArguments)!, "The manual crusher must resolve the exact InterestingME 1.0.16 muck API.");
var resolvedCompatibility = compatibilityArguments[0]!;
var setEntriesToFine = compatibilityType.GetMethod("SetCompositionEntriesToFine", BindingFlags.Instance | BindingFlags.NonPublic)!;
var entryConstructor = muckEntryType.GetConstructor(new[]
{
    typeof(string), typeof(int), typeof(string), typeof(string), typeof(float?), typeof(double?), typeof(bool)
})!;
var metadataEntry = entryConstructor.Invoke(new object?[]
{
    "game:ore-nativecopper", 1, "game:rock-granite", "coarse", new Nullable<float>(73.5f), new Nullable<double>(0.42), true
});
var entriesArray = Array.CreateInstance(muckEntryType, 1);
entriesArray.SetValue(metadataEntry, 0);
var compositionConstructor = muckCompositionType.GetConstructor(new[]
{
    typeof(IEnumerable<>).MakeGenericType(muckEntryType)
})!;
var metadataComposition = compositionConstructor.Invoke(new object?[] { entriesArray });
var metadataCompositionEntries = (IEnumerable)muckCompositionType.GetProperty("Entries")!.GetValue(metadataComposition)!;
var metadataCompositionEntry = metadataCompositionEntries.Cast<object>().Single();
var metadataProperties = new[] { "OreCode", "Count", "HostRockCode", "Concentration", "Availability", "Roasted" }
    .Select(name => muckEntryType.GetProperty(name, BindingFlags.Instance | BindingFlags.Public)!)
    .ToDictionary(property => property.Name, property => property.GetValue(metadataCompositionEntry));
Assert((bool)setEntriesToFine.Invoke(resolvedCompatibility, new[] { metadataComposition })!, "The manual conversion must update the fixture composition entries.");
var convertedProcessingVariant = (string)muckEntryType.GetProperty("ProcessingVariant")!.GetValue(metadataCompositionEntry)!;
Assert(convertedProcessingVariant == "fine", $"Successful conversion must change only the processing grade, got {convertedProcessingVariant}.");
foreach (var property in metadataProperties)
    Assert(Equals(property.Value, muckEntryType.GetProperty(property.Key)!.GetValue(metadataCompositionEntry)), $"Muck metadata property {property.Key} must remain unchanged.");
Assert((string)muckEntryType.GetProperty("OreCode")!.GetValue(metadataCompositionEntry)! == "game:ore-nativecopper", "Ore identity must survive fine conversion.");
Assert((int)muckEntryType.GetProperty("Count")!.GetValue(metadataCompositionEntry)! == 1, "Layer count must survive fine conversion.");
Assert((string)muckEntryType.GetProperty("HostRockCode")!.GetValue(metadataCompositionEntry)! == "game:rock-granite", "Host stone identity must survive fine conversion.");
Assert(Math.Abs((float)muckEntryType.GetProperty("Concentration")!.GetValue(metadataCompositionEntry)! - 73.5f) < 0.001f, "Concentration must survive fine conversion.");
Assert(Math.Abs((double)muckEntryType.GetProperty("Availability")!.GetValue(metadataCompositionEntry)! - 0.42d) < 0.001d, "Availability must survive fine conversion.");
Assert((bool)muckEntryType.GetProperty("Roasted")!.GetValue(metadataCompositionEntry)! , "Roasting state must survive fine conversion.");

var progress = NewManualProgress();
var progressProperty = manualProgressType.GetProperty("Strikes", BindingFlags.Instance | BindingFlags.NonPublic)!;
for (var strike = 1; strike <= 4; strike++)
    Assert(AdvanceManualStrike(progress, 0) == "Progress", $"Strike {strike} should advance manual muck progress without a cooldown.");
Assert((int)progressProperty.GetValue(progress)! == 4, "Four accepted strikes must leave shared progress at four.");
Assert(AdvanceManualStrike(progress, 0.1) == "Success", "An immediate fifth strike must resolve manual muck crushing successfully.");
Assert((int)progressProperty.GetValue(progress)! == 5, "The fifth accepted strike must reach the resolution threshold.");
var failedProgress = NewManualProgress();
progressProperty.SetValue(failedProgress, 4);
Assert(AdvanceManualStrike(failedProgress, 0.9) == "Failure", "An immediate high roll must resolve manual muck crushing as loss.");

var serializedPile = Activator.CreateInstance(muckPileType)!;
setManualProgress.Invoke(null, new object?[] { serializedPile, 3 });
var serializedTree = new Vintagestory.API.Datastructures.TreeAttribute();
saveManualProgress.Invoke(null, new object?[] { serializedPile, serializedTree });
Assert(serializedTree.GetInt("ime-olendril-patch:manual-muck-crusher-strikes", 0) == 3, "Manual progress must serialize with the muck block entity.");
var reloadedPile = Activator.CreateInstance(muckPileType)!;
loadManualProgress.Invoke(null, new object?[] { reloadedPile, serializedTree, null });
Assert((int)getManualProgress.Invoke(null, new[] { reloadedPile })! == 3, "Serialized manual progress must reload onto the pile, not a player.");
Assert((int)getManualProgress.Invoke(null, new[] { serializedPile })! == 3, "The same pile progress must be visible to another player.");
clearRemovedManualProgress.Invoke(null, new[] { reloadedPile });
Assert((int)getManualProgress.Invoke(null, new[] { reloadedPile })! == 0, "Removing a muck pile must clear manual progress.");
clearManualProgress.Invoke(null, new[] { serializedPile });
Assert((int)getManualProgress.Invoke(null, new[] { serializedPile })! == 0, "Resolving a muck pile must clear manual progress.");

var sourceText = File.ReadAllText(Path.Combine(repositoryRoot, "src", "ManualMuckCrusher.cs"));
Assert(sourceText.Contains("byEntity.World.Side != EnumAppSide.Server"), "Manual crushing must remain server-authoritative.");
Assert(sourceText.Contains("BlockAccessor.GetBlock(blockSel.Position)"), "The server must resolve the selected block from its position instead of relying on the client-only BlockSelection.Block field.");
Assert(sourceText.Contains("DamageItem(byEntity.World, byEntity, slot, 1"), "Each accepted manual strike must spend exactly one durability.");
Assert(sourceText.Contains("SendIngameError"), "Manual progress and errors must use the red in-game HUD message channel.");
Assert(sourceText.Contains("Lang.Get(messageKey"), "Manual HUD messages must pass translated text explicitly instead of displaying a localization slug.");
Assert(sourceText.Contains("GetHudText") && sourceText.Contains("Muck strike {arguments[0]}/{arguments[1]}"), "Manual HUD messages must have a readable literal fallback when localization is unavailable.");
Assert(!sourceText.Contains("CooldownMilliseconds") && !sourceText.Contains("ManualMuckStrikeOutcome.Cooldown"), "Manual crushing must not impose a strike cooldown.");
Assert(sourceText.Contains("Vintagestory.GameContent.ItemHammer") && sourceText.Contains("nameof(HeldAttackPrefix)"), "Manual crushing must patch the concrete vanilla hammer held-attack callback directly.");
Assert(!sourceText.Contains("PlayerUID") && !sourceText.Contains("PlayerName"), "Manual progress must not be keyed to an individual player.");

foreach (var path in materialNeedsPaths)
{
    Assert(ApplyPostfix(1, path), $"Tier 1 should accept Material Needs full block {path}.");
    Assert(!ApplyPostfix(2, path), $"Tier 2 should reject Material Needs full block {path}.");
    Assert(!ApplyPostfix(3, path), $"Tier 3 should reject Material Needs full block {path}.");
}

foreach (var path in glazedBrickPaths)
{
    Assert(ApplyPostfix(2, path), $"Tier 2 should accept Bricklayers full glazed brick {path}.");
    Assert(!ApplyPostfix(1, path), $"Tier 1 should reject Bricklayers glazed brick {path}.");
    Assert(!ApplyPostfix(3, path), $"Tier 3 should reject Bricklayers glazed brick {path}.");
    Assert(ApplyRoastingPostfix(path), $"The roasting-furnace validator should accept {path}.");
    Assert(ApplyRoastingPostfix(path, originalResult: true), $"Roasting acceptance must preserve an existing true result for {path}.");
}

foreach (var path in vanillaPaths.Concat(bricklayersPaths))
    Assert(ApplyPostfix(1, path, originalResult: true), $"An InterestingME-accepted full block {path} must remain accepted.");

foreach (var slabPath in new[]
{
    "brownmudbrick-light-slab", "graymudbrick-dark-slab", "redmudbrick-light-slab-north",
    "oystermudbrick-dark-stairs"
})
{
    Assert(!ApplyPostfix(1, slabPath), $"Tier 1 should reject non-full block {slabPath}.");
}

foreach (var excludedPath in new[]
{
    "glazedbrickslab-clear-black-down-free", "glazedbrickstairs-milky-yellow-north",
    "glazedtile-clear-polished-black", "glazedbricks-clear-black-item",
    "glazedbricks-clear-black-*", "bricklayers:glazedbricks-clear-black", "glazedbricks-clear-nope", ""
})
{
    Assert(!ApplyPostfix(2, excludedPath), $"Smelting Furnace must reject excluded glazed path {excludedPath}.");
    Assert(!ApplyRoastingPostfix(excludedPath), $"Roasting Furnace must reject excluded glazed path {excludedPath}.");
}
Assert(!ApplyRoastingPostfix(null), "A null roasting-furnace path must remain invalid.");
Assert(ApplyRoastingPostfix("mudbrick", originalResult: true), "Roasting acceptance must preserve InterestingME results.");

foreach (var unrelatedPath in new[]
{
    "materialneeds:redmudbrick-light", "materialneeds:stone", "redmudbrick-light-wall", "mudbrick-other"
})
{
    Assert(!ApplyPostfix(1, unrelatedPath), $"Tier 1 should reject unrelated path {unrelatedPath}.");
}

Assert(ApplyPostfix(1, "mudbrick", originalResult: true), "An InterestingME-accepted vanilla/Bricklayers mudbrick must remain accepted.");
Assert(ApplyPostfix(1, "mudbrick-slab", originalResult: true), "An InterestingME-accepted mudbrick slab result must remain untouched.");
Assert(!ApplyPostfix(1, null), "A null path must remain invalid.");
Assert(!ApplyPostfix(2, "redmudbrick-light", originalResult: false), "A Tier 2 mixed wall must not gain Material Needs Tier 1 blocks.");

var mixedTierOneWall = new[] { "mudbrick", "brownmudbrick-light", "graymudbrick-dark", "redmudbrick-light" };
foreach (var path in mixedTierOneWall)
    Assert(ApplyPostfix(1, path, originalResult: path == "mudbrick"), $"Mixed Tier 1 wall member {path} should validate consistently.");

var clientResults = materialNeedsPaths.Select(path => ApplyPostfix(1, path)).ToArray();
var serverResults = materialNeedsPaths.Select(path => ApplyPostfix(1, path)).ToArray();
Assert(clientResults.SequenceEqual(serverResults), "The side-independent allowlist must produce identical client/server results.");

var targetType = interestingAssembly.GetType("IME.BlockEntityLowTempFurnaceDoor")!;
var targetMethod = targetType.GetMethod(
    "IsValidTierBrick",
    BindingFlags.Static | BindingFlags.NonPublic,
    binder: null,
    types: new[] { typeof(int), typeof(string) },
    modifiers: null);
Assert(targetMethod is not null, "InterestingME 1.0.16 target method signature changed.");
Assert(targetMethod!.ReturnType == typeof(bool), "InterestingME target method must return bool.");
Assert(targetMethod.IsPrivate && targetMethod.IsStatic, "InterestingME low-temperature target must remain private static.");

var roastingType = interestingAssembly.GetType("IME.BlockEntityRoastingFurnaceDoor")!;
var roastingTargetMethod = roastingType.GetMethod(
    "IsValidBrick",
    BindingFlags.Static | BindingFlags.NonPublic,
    binder: null,
    types: new[] { typeof(string) },
    modifiers: null);
Assert(roastingTargetMethod is not null, "InterestingME roasting target method signature changed.");
Assert(roastingTargetMethod!.ReturnType == typeof(bool), "InterestingME roasting target method must return bool.");
Assert(roastingTargetMethod.IsPrivate && roastingTargetMethod.IsStatic, "InterestingME roasting target must remain private static.");

var system = (ModSystem)Activator.CreateInstance(systemType)!;
var api = DispatchProxy.Create<ICoreAPI, NullCoreApiProxy>();
system.Start(api);
var apiProxy = (NullCoreApiProxy)(object)api;
Assert(apiProxy.RegisteredBehaviorName == manualBehaviorName, "The mod system must register the manual crusher behavior name.");
Assert(apiProxy.RegisteredBehaviorType == manualCrusherType, "The mod system must register the manual crusher behavior type.");
Assert(
    apiProxy.LogMessages.Any(message => message.Contains("Manual muck crushing enabled", StringComparison.Ordinal)),
    "The real Harmony lifecycle must enable the manual crusher without a swallowed startup failure.");
Assert(
    !apiProxy.LogMessages.Any(message => message.Contains("Manual muck crushing disabled", StringComparison.Ordinal)),
    "The real Harmony lifecycle must not log that manual crushing was disabled.");
Assert(
    (bool)targetMethod.Invoke(null, new object?[] { 1, "redmudbrick-light" })!,
    "The registered Harmony postfix should make a Material Needs Tier 1 block valid.");
Assert(
    (bool)targetMethod.Invoke(null, new object?[] { 1, "mudbrick" })!,
    "The registered Harmony postfix must preserve InterestingME's accepted mudbrick result.");
foreach (var path in vanillaPaths.Concat(materialNeedsPaths).Concat(bricklayersPaths))
{
    Assert(
        (bool)targetMethod.Invoke(null, new object?[] { 1, path })!,
        $"The registered Harmony patch and InterestingME validator should accept Tier 1 full block {path}.");
}
foreach (var path in glazedBrickPaths)
{
    Assert(
        (bool)targetMethod.Invoke(null, new object?[] { 2, path })!,
        $"The registered Smelting Furnace patch should accept Tier 2 glazed brick {path}.");
    Assert(
        (bool)roastingTargetMethod.Invoke(null, new object?[] { path })!,
        $"The registered Roasting Furnace patch should accept glazed brick {path}.");
}
Assert(
    !(bool)targetMethod.Invoke(null, new object?[] { 2, "redmudbrick-light" })!,
    "The registered Harmony postfix must not make Material Needs blocks valid in Tier 2.");
system.Dispose();
Assert(
    !(bool)targetMethod.Invoke(null, new object?[] { 1, "redmudbrick-light" })!,
    "Disposing the mod system must remove its Harmony patch.");
Assert(
    !(bool)roastingTargetMethod.Invoke(null, new object?[] { glazedBrickPaths[0] })!,
    "Disposing the mod system must remove its roasting-furnace Harmony patch.");

using (var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(repositoryRoot, "modinfo.json"))))
{
    var root = manifest.RootElement;
    Assert(root.GetProperty("modid").GetString() == "ime-olendril-patch", "The mod ID must be ime-olendril-patch.");
    Assert(root.GetProperty("side").GetString() == "Universal", "The mod must declare Universal loading.");
    Assert(root.GetProperty("version").GetString() == "1.3.9", "The mod version must be 1.3.9.");
    Assert(root.GetProperty("description").GetString()!.Contains("manual crushing"), "The manifest must mention manual crushing.");
    var dependencies = root.GetProperty("dependencies");
    Assert(dependencies.GetProperty("interestingme").GetString() == "1.0.16", "InterestingME dependency must be exact.");
    Assert(dependencies.GetProperty("materialneeds").GetString() == "2.0.0", "Material Needs dependency must be exact.");
    Assert(dependencies.GetProperty("bricklayers").GetString() == "3.2.2", "Bricklayers dependency must be exact.");
}

Console.WriteLine($"PASS: {recipeCodes.Length} explicit door recipes, {materialNeedsPaths.Length} Material Needs variants, Bricklayers compatibility, manual muck hammer gates/progression/serialization/metadata, tier gates, exclusions, signatures, and universal manifest checks.");

class NullCoreApiProxy : DispatchProxy
{
    private ILogger? logger;

    internal string? RegisteredBehaviorName { get; private set; }
    internal Type? RegisteredBehaviorType { get; private set; }
    internal IReadOnlyList<string> LogMessages => logger is null
        ? Array.Empty<string>()
        : ((NullLoggerProxy)(object)logger).Messages;

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod?.Name == "get_Logger")
        {
            logger ??= DispatchProxy.Create<ILogger, NullLoggerProxy>();
            return logger;
        }
        if (targetMethod?.Name == "RegisterCollectibleBehaviorClass")
        {
            RegisteredBehaviorName = (string?)args?[0];
            RegisteredBehaviorType = (Type?)args?[1];
            return null;
        }

        if (targetMethod?.ReturnType == typeof(void)) return null;
        return targetMethod?.ReturnType.IsValueType == true
            ? Activator.CreateInstance(targetMethod.ReturnType)
            : null;
    }
}

class NullLoggerProxy : DispatchProxy
{
    internal List<string> Messages { get; } = new();

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (args?.FirstOrDefault() is string message) Messages.Add(message);
        return null;
    }
}
