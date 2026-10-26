# prebuilt/

`InFalsusAdvVN.dll` — the assembly built from the sources in this repository
(`dotnet build -c Release`), committed so the release workflow can package it.

## Why this exists

Building this mod requires the **game's own interop assemblies** (`Game.dll`, `Str.dll`,
`Game.NovelEngine.dll`, `Game.Data.dll`, `Game.Input.dll`, `FastText.dll` and the Unity
modules), which Il2CppInterop generates from a local **In Falsus** installation. Those
assemblies belong to the game and are not part of this repository, so a GitHub runner cannot
compile the project. Binary-building on CI would require redistributing them.

Therefore:

* `build/package.ps1` builds from source whenever the interop assemblies are present
  (i.e. on a developer machine), and
* falls back to this committed assembly otherwise (CI).

## Refreshing it

On a machine with the game installed:

```powershell
dotnet build -c Release
Copy-Item bin\Release\InFalsusAdvVN.dll prebuilt\InFalsusAdvVN.dll -Force
```

Do this in the same commit that changes the sources or the version, otherwise the packaged
mod will not match the tag it is released under.

## Verifying

```powershell
Get-FileHash prebuilt\InFalsusAdvVN.dll -Algorithm SHA256
.\build\package.ps1 -SkipBuild -ExpectedTag v<version>
```

Current build: `1.0.5`, SHA-256 `C9012D85806EAABF25A3BBA3230A99EC85F4E8CAE063ED6B3EF0958E82D480BD`.
