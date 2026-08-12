# AGENTS.md

## Project overview

This repository contains a small universal Vintage Story compatibility mod. It patches InterestingME 1.0.16 with Harmony so Material Needs 2.0.0 full mudbrick blocks are accepted in Tier 1 low-temperature furnace structures.

## Repository layout

- `src/` contains the mod source and its .NET 10 project.
- `tests/` contains an executable integration-check harness rather than a conventional unit-test project.
- `build/package.sh` builds and packages the distributable ZIP.
- `modinfo.json` is the Vintage Story mod manifest.
- `references/` contains ignored upstream mod artifacts used for compatibility checks. Treat these files as read-only fixtures.
- `artifacts/` is generated output and is ignored by Git.

## Prerequisites

- Use the .NET SDK compatible with `net10.0`.
- Set either `VintageStoryPath` or `VINTAGE_STORY_PATH` to a Vintage Story installation containing `VintagestoryAPI.dll` and `Lib/0Harmony.dll`.
- Do not add game or upstream mod binaries to source control.

## Build and verification

Run commands from the repository root.

```bash
dotnet build src/InterestingMeMaterialNeedsFurnacePatch.csproj --configuration Release
dotnet run --project tests/InterestingMeMaterialNeedsFurnacePatch.Tests.csproj --configuration Release
```

The test harness expects the Release mod assembly to have been built first and expects the InterestingME 1.0.16 fixture under `references/`.

To create the release archive:

```bash
build/package.sh Release
```

The package is written beneath `artifacts/package/`.

## Change guidelines

- Keep the patch narrowly scoped: preserve any result already accepted by InterestingME and only extend Tier 1 validation for the exact Material Needs full-block paths.
- Do not broaden the allowlist to slabs, stairs, walls, namespaced strings, or higher furnace tiers without an explicit requirement.
- Keep the Harmony target type and method signature checks explicit. If upstream compatibility changes, fail safely and log a warning rather than breaking mod startup.
- Retain universal client/server behavior; validation must be deterministic and side-independent.
- Unpatch the Harmony instance during disposal.
- Update `modinfo.json`, source behavior, and integration checks together when changing supported dependency versions or accepted block paths.
- Follow the existing C# style: file-scoped namespaces, nullable reference types, implicit usings, four-space indentation, and braces for type and method bodies.
- Avoid unrelated formatting or generated-file changes.

## Testing expectations

- Add or update checks in `tests/Program.cs` for every behavior change.
- Cover positive cases, tier exclusions, non-full-block exclusions, preservation of original accepted results, Harmony registration/removal, and manifest constraints as applicable.
- Build and run the integration harness before handing off changes. If the local Vintage Story installation is unavailable, report that verification limitation explicitly.
