# dotnet test harness

Runs the package's tests and a scripted end-to-end scenario with plain `dotnet test`, without Unity.
Unity ignores this folder because its name ends in `~`.

Each project compiles one assembly's source and references only what that assembly's `.asmdef`
lists, so a dependency that is not allowed (for example the Jev backend using the SparseVr backend,
or the calibration assembly using Unity) fails to compile. The Unity types are replaced by small
stand-ins in `Stubs/`.

```
dotnet test Tests/Tests.csproj
```

Needs the .NET 8 SDK. Everything is compiled with `LangVersion 9.0`, the level Unity 6 uses.

| Path | Content |
|---|---|
| `Stubs` | stand-ins for the Unity, Inference Engine and Meta types the package uses |
| `Calibration`, `Backends`, `SparseVr`, `Jev`, `Runtime` | one project per assembly |
| `Tests` | the package's `Tests/Editor` files plus the harness-only tests |
| `Tests/PublicApiContract.cs` | compiles every member that existing projects use, and pins the frozen values |
| `Tests/GoldenScenario.cs` | drives the predictor end to end and can write a transcript (`GOLDEN_OUT=<file>`) |
| `Tests/OrchestratorTests.cs`, `BackendAssembliesTests.cs`, `DeviceManagerHarnessTests.cs` | behaviour of the predictor, backends and devices |
| `Fixtures` | files written by earlier releases, to prove they still load |
| `UnityOnlyCheck` | compile check (not run) of the two tests that need the real ONNX model |
| `AndroidCheck` | compile check of the Android-only branch of the sound device |

Compare two versions of the package by running the scenario on each and diffing the transcripts:

```
GOLDEN_OUT=old.json dotnet test Tests/Tests.csproj -p:PackageRoot=/path/to/old/package
GOLDEN_OUT=new.json dotnet test Tests/Tests.csproj
```

(`PackageRoot` defaults to this package. Tests that need the new layout are skipped for an older one.)

This does not replace the EditMode tests in Unity: it cannot check asmdef import, the real ONNX model,
the editor menu or a real network call.
