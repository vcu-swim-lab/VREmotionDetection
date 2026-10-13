using System.Runtime.CompilerServices;

// The EditMode test assembly needs to reach `Utils.PrepareModel`, `Device` and
// `DeviceManager.PollAndDispatch`, all of which are internal on purpose: they are
// implementation details of the package, not part of its public surface.
[assembly: InternalsVisibleTo("VREmotionDetection.Editor.Tests")]
