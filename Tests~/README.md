# Tests

Everything that checks the package, runnable without Unity:

```
bash Tests~/run-tests.sh                     # all stages
bash Tests~/run-tests.sh --filter JevText    # only tests whose name matches
bash Tests~/run-tests.sh --compare 6416b0c   # also diff the end-to-end transcript against an older git ref
bash Tests~/run-tests.sh --help
```

Needs the .NET 8 SDK. The script looks for `dotnet` on `PATH`, then `$DOTNET`, `$DOTNET_ROOT`, `~/dotnet-sdk`.
NuGet packages are pinned (NUnit 3.14.0, Newtonsoft.Json 13.0.3, ...) and restore from `~/.nuget/packages` when offline.
It exits non-zero if any stage fails and writes logs and a `.trx` to `DotnetHarness/artifacts/` (ignored by git).
The tests run with their own temp folder, which the script removes.

| Stage | What it checks |
|---|---|
| `preflight` | finds `dotnet`; says whether `Runtime/`, `Editor/`, `Samples~/` have uncommitted changes (the tests never modify them) |
| `restore` | NuGet restore of the 11 projects |
| `compile` | each package assembly alone, built from its `.asmdef` rules (so a forbidden dependency, e.g. Calibration using Unity, fails); the Android-only branch of the sound device; the two Unity-only tests; every recipe in `Documentation~/UsageGuide.md`, the README snippets and `Samples~/` against the real public API |
| `tests` | `dotnet test`: the files in `EditMode/` and `DotnetHarness/Tests/` |
| `golden` | only with `--compare <ref>`: runs a scripted end-to-end scenario on that older version and on this one and diffs the transcripts. Informational |

## Layout

```
Tests~/
  run-tests.sh
  EditMode/          Unity EditMode (NUnit) tests that need no Unity: backends, Jev parsing/requests/options, calibration,
                     devices, ring buffer, adb output, FaceAuNames. Compiled into the dotnet test project.
  DotnetHarness/     project files, stand-ins for Unity / Inference Engine / OVR / UnityEditor, and the harness-only tests
    Tests/           predictor end to end (orchestrator, cancellation, calibration interruption), backends, transport,
                     results file, public API contract, docs-vs-examples sync, golden scenario
    ReadmeExamples/  the recipes of the usage guide, compiled against the real API
    Fixtures/        a calibration file written by an earlier release, to prove it still loads
```
`DotnetHarness/README.md` explains how the projects are built.

## What it does not cover

- **A real Unity run.** `EditMode/ModelPreparationTests.cs` and `EditMode/ModelCalibrationEndToEndTests.cs` need the real
  ONNX model and Inference Engine; here they are only compiled (`UnityOnlyCheck`). Asmdef import, the editor menu, a real
  network call and the headset are not exercised either. The shipped package no longer grants its internals to a test
  assembly, so running `EditMode/` inside Unity would need that `InternalsVisibleTo` added back (in a copy, not in the package).
- **Rendering of `UnityEngine` behaviour.** The stand-ins are simple (for example `Awaitable.NextFrameAsync` is a 1 ms delay),
  and `Worker` returns canned scores. They prove the package's own logic, not Unity's.

## Why some tests differ from the ones on `jev_branch`

The tests were removed from the package in `214fe73` and the library moved on. Restored, they needed:

| Change in the library | What changed in the tests |
|---|---|
| Types moved into `VcuSwimLab.VREmotionDetection` (`6416b0c`) | `using VcuSwimLab.VREmotionDetection;` in most files |
| `InternalsVisibleTo` for the test assembly removed | the harness grants it itself (`DotnetHarness/Harness/InternalsVisibleTo.cs`) |
| `DeviceRegistry.Replace`, `DeviceManager.Disable` removed | `EditMode/DeviceRegistryHook.cs` reaches the registry's private dictionary; `devices.Remove` replaces `Disable` |
| `ResultsRecorder(folder)` removed; folder comes from `Application.persistentDataPath` | `ResultsRecorderTests` moved to `DotnetHarness/Tests` and sets the (stubbed) path |
| Jev error text is hidden unless `IncludeServerDetails` is on | `JevResponseTests` asserts both modes |
| `UseSparseVr()` became an obsolete alias of `UseLocalModel()` | examples use the new name; the old one is still compile-checked |
| `Canceled`, `ICancelableBackend`, `ICancelableHttpTransport` | stub gained `UnityWebRequest.Abort()` and a cancellable `WaitForSecondsAsync`; new tests |

The harness project files (`*.csproj`) were never committed on `jev_branch` because `.gitignore` ignores `*.csproj`; the
root `.gitignore` now un-ignores the ones under `Tests~/DotnetHarness`.

## Keeping it green

- Changed a recipe in `Documentation~/UsageGuide.md`? `DocsRecipeSyncTests` fails until the matching file in
  `DotnetHarness/ReadmeExamples/` is the same text, which in turn is compiled against the API.
- Added a `PredictionErrorCode`? `BackendContractTests` makes you decide whether it is transient.
- A test that must read static state (`Time.Override`, `Worker.*`, `UnityWebRequest.Handler`, `Debug.Logs`...) shares it with
  the others: tests run one at a time and each rig resets what it uses.

## Behaviour worth knowing (documented by tests, not changed)

- Returning to the local model while its abandoned inference is still reading back is answered `Busy` by the backend until
  that inference ends (`Returning_to_the_local_model_while_its_abandoned_inference_is_still_running_is_Busy_until_it_ends`).
- A Jev answer above 1 is refused with no tolerance: `1.0000001` is an `InvalidResponse` (`An_answer_outside_zero_to_one_is_refused...`).
