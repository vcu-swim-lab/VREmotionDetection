# dotnet test harness

Runs the package's tests and a scripted end-to-end scenario with plain `dotnet`, without Unity. Unity ignores this folder
because its name ends in `~`. Use `../run-tests.sh`; this file explains how the projects fit together.

Each project compiles one assembly's source and references only what that assembly's `.asmdef` lists, so a dependency that
is not allowed (for example the Jev backend using the SparseVr backend, or the calibration assembly using Unity) fails to
compile. The Unity types are replaced by small stand-ins.

```
Stubs          UnityEngine, Unity.InferenceEngine, Unity.Collections, OVRFaceExpressions (+ Newtonsoft for JsonUtility)
EditorStubs    UnityEditor, referenced only by the Editor assembly and the Unity-only check
Calibration    Runtime/Calibration                          no Unity reference at all (noEngineReferences)
Backends       Runtime/Backends/*.cs                        -> Calibration
SparseVr       Runtime/Backends/SparseVr                    -> Backends, Calibration
Jev            Runtime/Backends/Jev                         -> Backends, Calibration, Newtonsoft
Runtime        Runtime/AssemblyInfo.cs + Runtime/Scripts    -> all of the above
Editor         Editor/*.cs                                  -> Runtime
Tests          ../EditMode + Tests/                         -> everything (assembly VREmotionDetection.Editor.Tests)
Golden         Tests/GoldenScenario.cs + HarnessRig.cs      -> the package, for comparing with older versions
UnityOnlyCheck compile check (not run): the two tests that need the real ONNX model, built with UNITY_EDITOR
AndroidCheck   compile check: the Android branch of the sound device, built with UNITY_ANDROID
ReadmeExamples compile check: usage-guide recipes and Samples~/
```

- Everything is compiled with `LangVersion 9.0` (Unity 6) and `net8.0`; build output goes to `artifacts/` (git-ignored).
- The shipped package grants no internals to a test assembly, so `Harness/InternalsVisibleTo.cs` is compiled into each
  package assembly and names `VREmotionDetection.Editor.Tests`.
- `PackageRoot` defaults to the repository root and can point at another checkout:
  `dotnet test Tests/Tests.csproj -p:PackageRoot=/path/to/other/package`.
- `GOLDEN_OUT=<file>` makes the golden scenario write its transcript; `../run-tests.sh --compare <ref>` does this for an
  older version and for the current one and diffs them. For a version from before the types moved into a namespace
  it passes `-p:LegacyGlobalNamespace=true`; for a version with an older namespace prefix it renames that prefix to
  `VcuSeCenter` in its temporary copy of that version.
- `Fixtures/released_calibration.json` is a file written by an earlier release; it is copied next to the test assembly.
- Tests share static stand-in state (`Time.Override`, `Worker.*`, `UnityWebRequest.Handler`, `Debug.Logs`), so they must
  not run in parallel.
