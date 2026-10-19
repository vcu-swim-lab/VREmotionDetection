using System.Runtime.CompilerServices;

// Harness-only: the shipped package no longer grants its internals to a test assembly, so the harness does it for
// every assembly it builds from package source. Only the test assembly is named.
[assembly: InternalsVisibleTo("VREmotionDetection.Editor.Tests")]
