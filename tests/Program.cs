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

if (!File.Exists(modPath)) throw new FileNotFoundException("Build the mod before running tests.", modPath);
if (!File.Exists(interestingMePath)) throw new FileNotFoundException("InterestingME reference is missing.", interestingMePath);

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
var systemType = modAssembly.GetType("InterestingMeMaterialNeedsFurnacePatch.MaterialNeedsFurnacePatchSystem")!;
var lowTempPostfix = systemType.GetMethod("IsMaterialNeedsTierOneMudbrick", BindingFlags.Static | BindingFlags.NonPublic)!;
var glazedLowTempPostfix = systemType.GetMethod("IsBricklayersTierTwoGlazedBrick", BindingFlags.Static | BindingFlags.NonPublic)!;
var roastingPostfix = systemType.GetMethod("IsRoastingGlazedBrick", BindingFlags.Static | BindingFlags.NonPublic)!;

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

var interestingAssembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(interestingMePath);
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
    Assert(root.GetProperty("version").GetString() == "1.2.0", "The mod version must be 1.2.0.");
    Assert(root.GetProperty("description").GetString()!.Contains("Screen bronze compatibility"), "The manifest must mention Screen bronze compatibility.");
    var dependencies = root.GetProperty("dependencies");
    Assert(dependencies.GetProperty("interestingme").GetString() == "1.0.16", "InterestingME dependency must be exact.");
    Assert(dependencies.GetProperty("materialneeds").GetString() == "2.0.0", "Material Needs dependency must be exact.");
    Assert(dependencies.GetProperty("bricklayers").GetString() == "3.2.2", "Bricklayers dependency must be exact.");
}

Console.WriteLine($"PASS: {recipeCodes.Length} explicit door recipes, {materialNeedsPaths.Length} Material Needs variants, Bricklayers compatibility, tier gates, exclusions, preservation, signature, and universal manifest checks.");

class NullCoreApiProxy : DispatchProxy
{
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod?.Name == "get_Logger") return DispatchProxy.Create<ILogger, NullLoggerProxy>();
        return targetMethod?.ReturnType.IsValueType == true
            ? Activator.CreateInstance(targetMethod.ReturnType)
            : null;
    }
}

class NullLoggerProxy : DispatchProxy
{
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => null;
}
