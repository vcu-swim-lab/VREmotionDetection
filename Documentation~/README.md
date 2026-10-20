# VREmotionDetection documentation

| Page | Read it to |
|---|---|
| [Detailed usage guide](UsageGuide.md) | find complete recipes, the API reference, return types, troubleshooting, and privacy details |
| [Architecture](Architecture.md) | see how the package is split into assemblies and how a prediction flows |
| [Backends](Backends.md) | use a backend, switch between them, or write your own |
| [Jev](Jev.md) | set up Typesafe's Jev model, including the API key and privacy |
| [Calibration](Calibration.md) | understand and use per-player calibration |
| [Diagnostics](Diagnostics.md) | handle errors, read the error codes, understand the logging |
| [Data formats](DataFormats.md) | read the model input and output, results CSV and calibration file |
| [Design notes](DesignNotes.md) | learn why things are the way they are |
| [Migration](Migration.md) | move from 2.1 to 3.0 |

The package README has the quick start. All runtime types are in the namespace `VcuSeCenter.VREmotionDetection`: add `using VcuSeCenter.VREmotionDetection;` to your scripts. Backend types are in `VcuSeCenter.Backends` (and `VcuSeCenter.Backends.Jev`, `VcuSeCenter.Backends.SparseVr`), calibration types in `VcuSeCenter.Calibration`.
