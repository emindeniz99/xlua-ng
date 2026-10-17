# Source generator spike: Roslyn 4.3.1 in Unity 2021.3 to 6000.6

Measured on 2026-10-17. Question: can xlua-ng swap the Editor "Generate Code" menu for
a Roslyn source generator that writes the C# to Lua wrappers at compile time? Here
that means an `IIncrementalGenerator` built against Microsoft.CodeAnalysis.CSharp
4.3.1, shipped inside a UPM package. Reflection stays as the opt-in fallback.

Short answer: yes, in every supported editor. The generator loads, runs, reports
diagnostics and regenerates after an edit in 2021.3.45f2, 2022.3.62f3, 6000.0.85f1,
6000.3.26f1 and 6000.6.6f1. Its `[RuntimeInitializeOnLoadMethod]` registration runs in
a Mono player with High stripping. It also survives an IL2CPP High-stripping build,
which was checked statically. No 3.8 `ISourceGenerator` fallback was needed.

Sources: `Tools/SourceGenSpike/`. Projects, logs, players and result JSON:
`/Volumes/T7Data/ext-ai-projects/xlua-ng/sourcegen-spike/`.

## What was built

- `Generator/`: `XLuaTest.Generator.dll`, netstandard2.0, `IIncrementalGenerator`,
  Microsoft.CodeAnalysis.CSharp 4.3.1 (`PrivateAssets=all`, nothing copied next to
  it). It reads `[assembly: XLuaTest.Export(typeof(T))]` and `[XLuaTest.Export]` on
  types through `ForAttributeWithMetadataName`. In 4.3.1 this API handles
  assembly-level attributes: the target node is the `CompilationUnitSyntax` and the
  target symbol is the assembly. For each assembly it emits:
  - `XLuaTest.Generated.XLuaSGMarker_<assembly>`, always. This shows which assemblies
    the generator ran in.
  - `XLuaTest.Generated.XLuaGen_<assembly>`, a non-partial `internal static class`
    emitted only when there are exports and `XLuaTest.Registry` is visible. It calls
    one static member of each exported type directly (`UserThing.Answer()`,
    `Vector3.back`, `Time.captureDeltaTime`, `UserGameThing.Hello`). It registers
    itself from `[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]`.
  - XLSG001 (warning) for `[Export]` on a generic type definition, and XLSG002
    (error) for an exported type that generated code cannot access.

  It does not call `RegisterPostInitializationOutput`: the attributes live in the
  runtime assembly.
- `Package/com.xlua-ng.sgspike`: the runtime asmdef `XLuaTest.Runtime`
  (`ExportAttribute`, `Registry`). `run_spike.py` adds the DLL with a generated
  `.meta`: label `RoslynAnalyzer`, Any Platform off, Editor and Standalone excluded.
  It is installed as a `file:` tarball, so it is immutable and lives in
  `Library/PackageCache` the way a registry package would.
- `Project/`: the consumer project.
  - `Assets/Game` (Assembly-CSharp, no asmdef).
  - `Assets/UserGame/UserGame.asmdef`, which references `XLuaTest.Runtime`.
  - `Assets/Unrelated/Unrelated.asmdef`, which does not reference it. It declares
    its own internal `XLuaTest.ExportAttribute` and uses it. If the generator ran
    there, it would leave a marker.
  - `Assets/Editor/SpikeProbe.cs`: the batch methods.

  On first open Unity adds its default packages (uGUI, TMP, Timeline, Purchasing,
  Services and so on). The scope check therefore also covers 52 to 62 Unity package
  assemblies.

## Results

| | 2021.3.45f2 | 2022.3.62f3 | 6000.0.85f1 | 6000.3.26f1 | 6000.6.6f1 |
|---|---|---|---|---|---|
| Unity csc (Roslyn) | 4.3.1 | 4.3.1 | 4.3.1 | 4.3.1 | 4.10.0 |
| Editor compile, `XLUA_SG_PASS` | pass | pass | pass | pass | pass |
| Runs in Assembly-CSharp, UserGame, XLuaTest.Runtime, Assembly-CSharp-Editor | yes | yes | yes | yes | yes |
| Runs in Unrelated / Unity package assemblies | no / no (52) | no / no (59) | no / no (61) | no / no (62) | no / no (62) |
| Analyzer load problems (CS8032, CS8784, CS8785, CS9057) | none | none | none | none | none |
| XLSG001 warning in the log, with file and line | yes | yes | yes | yes | yes |
| XLSG002 error fails the compile, with file and line | yes | yes | yes | yes | yes |
| CS0436 | none | none | none | none | none |
| Edit in the same editor session regenerates (`XLUA_SG_INCREMENTAL_PASS`) | pass | pass | pass | pass | pass (see note 3) |
| Mono macOS arm64 player, High stripping: both registrations ran, direct calls returned | pass | pass | pass | pass | pass |
| Reflection-only member (names built at run time) after High stripping | stripped | stripped | stripped | stripped | stripped |

The pass criteria for "Editor compile" were checked by reflection over the loaded
assemblies:

- both wrappers exist, are `internal static`, and have the per-assembly name;
- the exports are correct;
- `Register` carries `[RuntimeInitializeOnLoadMethod]`;
- `Invoke()` returns `Game.UserThing.Answer()=42;UnityEngine.Vector3.back=(...)` and
  `...;UserGame.UserGameThing.Hello=hello`;
- there is no marker in `Unrelated`.

The incremental run edits `GameExports.cs` (`Version = 1` and a new
`[Export] class UserThing2`) and calls `AssetDatabase.Refresh`. The wrapper then
reports `Answer()=43` and `Game.UserThing2.Name=two`, about 2 s after the edit in
every version.

### IL2CPP, High stripping (6000.3.21f1, Android ARM64, exported Gradle project)

6000.3.26f1 has no Android or iOS module, so this ran on 6000.3.21f1, which has the
same Roslyn. The build succeeded, and the generator warning showed in the build log.
The phone was not used, so this result is static, from `inspect_il2cpp.py`:

- `RuntimeInitializeOnLoads.json` lists `XLuaGen_Assembly_CSharp.Register` and
  `XLuaGen_UserGame.Register` with `loadTypes=1` (BeforeSceneLoad).
- `ManagedStripped/Assembly-CSharp.dll` and `UserGame.dll` keep the wrapper,
  `Register`, `Invoke` and `UserThing.Answer`. High stripping removed
  `UserThing.OnlyReflected`, the marker class and `GenericThing<T>`. The same holds
  in `global-metadata.dat`.
- In the IL2CPP C++, `XLuaGen_Assembly_CSharp_Invoke` calls
  `UserThing_Answer_m...(NULL)` and `Vector3_get_back_m..._inline(NULL)` directly.
  `XLuaGen_..._Register` calls `Registry_Register_m...`.

Not done: running an IL2CPP player. macOS IL2CPP is not installed in any editor, the
phone was off limits, and an iOS simulator run would have needed several GB of new
swap with the startup disk at 25.3 GB (limit 25). Next step is to run
`SpikeProbe.BuildIosSimulator` from `run_spike.py ios`, then `xcodebuild` and
`simctl launch --console`, on a machine with headroom.

### Shipping: `RunOnlyOnAssembliesWithReference`

The label is not a Unity label. The string is not in any of the five editors: not in
the native binary, not in `UnityEditor.CoreModule.dll` (ASCII or UTF-16), not anywhere
in the 6000.3.26f1 bundle. To test it anyway, the DLL went into an `Analyzers/`
package folder with no asmdef above it and labels `RoslynAnalyzer` +
`RunOnlyOnAssembliesWithReference` (variant `rootlabel`). The generator then ran in
every assembly: 56 of 56 in 2021.3.45f2 and 66 of 66 in 6000.3.26f1, including
`Unrelated`, where it picked up the local attribute. The probe failed as intended.

Scoping comes only from the asmdef rule: an analyzer in a folder with an asmdef (or
below it) applies to that assembly and to assemblies that reference it. The
generated `.csproj` files follow the same rule, so IDEs see the generator only where
Unity runs it.

## Notes

1. The 2021.3 and 2022.3 manuals ask for Microsoft.CodeAnalysis 3.8. That is the
   floor Unity documents, not what the compiler is. Every editor tested ships csc
   4.3.1 or later in `DotNetSdkRoslyn`, and a 4.3.1 generator loads there. The 3.7
   Roslyn under `MonoBleedingEdge/.../msbuild` is not used for script compilation.
   Unity's patch notes are the only source for which 2021.3 or 2022.3 patch first
   shipped 4.3.1. Only the patches listed above were tested.
2. A generator that cannot load does not stop the compile. As a negative control, the
   same generator built against 4.10.0 (`-p:RoslynVersion=4.10.0`) went into
   6000.3.26f1. Unity printed `warning CS9057: The analyzer assembly ... references
   version '4.10.0.0' of the compiler, which is newer than the currently running
   version '4.3.0.0'`, compiled everything, and produced no wrappers. The probe
   failed. The runtime therefore needs a check: if no `XLuaGen_*` registration ran,
   log it once and use reflection.
3. 6000.6.6f1 reloads the domain inside `AssetDatabase.Refresh` during
   `-executeMethod`. Afterwards neither `EditorApplication.delayCall` nor `update`
   fires in batch mode. The first incremental run hung until the 1200 s timeout.
   That was a harness problem, not a generator problem: the generator had already
   regenerated, and the next player build saw `UserThing2`. Phase 2 now runs from
   the `[InitializeOnLoad]` constructor, and the rerun passed. The other four
   versions passed with `delayCall`.
4. UnityLinker keeps members it can see through `typeof(T).GetMethod("literal")`, and
   with a non-constant name it keeps all public methods of `T`. Only a type and
   member name built at run time (as Lua supplies them) shows the stripping. With
   High stripping, Mono and IL2CPP both drop such members, so reflection mode needs
   `link.xml`. Generated direct calls do not.
5. 6000.x compiles through a long-lived VBCSCompiler server. Not tested: whether
   replacing the generator DLL during an editor session (a package update) takes
   effect before a restart.
6. `ForAttributeWithMetadataName` needs 4.3.0, and the Unity Roslyn floor is 4.3.1,
   so 4.3.x is the only usable reference version. Note 2 shows what happens with
   anything newer.

## Recommendation

Make the source generator mode available from **Unity 2021.3** (tested on
2021.3.45f2), built against Microsoft.CodeAnalysis.CSharp **4.3.1** and no newer:

- `IIncrementalGenerator` + `ForAttributeWithMetadataName`;
- no `RegisterPostInitializationOutput`;
- the DLL next to the xLua runtime asmdef, with the `RoslynAnalyzer` label and all
  platforms off.

If the supported floor stays at 2021.3, document "latest 2021.3 / 2022.3 patch".
MessagePack and MemoryPack choose 2022.3.12f1 as their minimum. Keep reflection as
the fallback for older or failing setups, and detect the missing registration at
run time.

## Reproduce

```sh
Tools/SourceGenSpike/build_generator.sh
python3 Tools/SourceGenSpike/run_spike.py 2021.3.45f2 prepare compile error incremental mono
python3 Tools/SourceGenSpike/run_spike.py 6000.3.26f1 prepare compile --variant rootlabel
python3 Tools/SourceGenSpike/run_spike.py 6000.3.21f1 prepare android
python3 Tools/SourceGenSpike/inspect_il2cpp.py <project> <players/.../android-export>
python3 Tools/SourceGenSpike/summarize.py
```

Every Unity editor and player launch goes through `locked_run.sh`. It:

- holds `/Volumes/T7Data/ext-ai-projects/xlua-ng/native-test.lock`;
- refuses to start next to another editor;
- runs the child in its own process group with a timeout;
- releases the lock only after the group is gone;
- runs `pkill -x xctest` afterwards;
- stops below 25 GB free on the startup disk.

`TMPDIR` points into the spike directory.
