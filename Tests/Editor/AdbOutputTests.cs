using System;
using System.IO;
using NUnit.Framework;

public class AdbOutputTests
{
    [Test]
    public void Devices_are_read_from_tab_separated_lines_and_daemon_noise_is_ignored()
    {
        string text = "* daemon not running; starting now at tcp:5037\n* daemon started successfully\n" +
                      "List of devices attached\nQUEST123\tdevice\nPHONE9\tunauthorized\nOLD1\toffline\n\n";

        var devices = AdbOutput.ParseDevices(text);

        CollectionAssert.AreEqual(new[] { "QUEST123" }, devices.Ready);
        Assert.IsTrue(devices.Unauthorized);
    }

    [Test]
    public void No_devices_and_empty_output_give_nothing_ready()
    {
        Assert.IsEmpty(AdbOutput.ParseDevices("List of devices attached\n\n").Ready);
        Assert.IsEmpty(AdbOutput.ParseDevices("").Ready);
        Assert.IsEmpty(AdbOutput.ParseDevices(null).Ready);
        Assert.IsFalse(AdbOutput.ParseDevices(null).Unauthorized);
    }

    [Test]
    public void Two_ready_devices_are_both_reported()
    {
        var devices = AdbOutput.ParseDevices("List of devices attached\nA\tdevice\nB\tdevice\n");

        CollectionAssert.AreEqual(new[] { "A", "B" }, devices.Ready);
    }

    [TestCase("adb: error: failed to stat remote object '/x': No such file or directory", true)]
    [TestCase("remote object '/x' does not exist", true)]
    [TestCase("adb: error: device offline", false)]
    [TestCase(null, false)]
    public void A_missing_results_folder_is_recognised(string output, bool expected)
    {
        Assert.AreEqual(expected, AdbOutput.IsMissingRemoteFolder(output));
    }

    [TestCase("/sdcard/.../results/.: 3 files pulled, 0 skipped. 1.2 MB/s", 3)]
    [TestCase("/sdcard/.../results/.: 1 file pulled, 0 skipped.", 1)]
    public void The_number_of_pulled_files_is_read_from_the_summary(string output, int expected)
    {
        Assert.AreEqual(expected, AdbOutput.PulledFileCount(output));
    }

    [Test]
    public void An_unrecognised_summary_gives_no_count()
    {
        Assert.IsNull(AdbOutput.PulledFileCount("nothing useful"));
        Assert.IsNull(AdbOutput.PulledFileCount(null));
    }

    [Test]
    public void The_remote_folder_matches_where_the_predictor_saves_results()
    {
        Assert.AreEqual("/sdcard/Android/data/com.example.game/files/VREmotionDetection/results",
                        AdbOutput.RemoteResultsFolder("com.example.game"));
        Assert.Throws<ArgumentException>(() => AdbOutput.RemoteResultsFolder(" "));
        StringAssert.EndsWith(Path.Combine("VREmotionDetection", "results"), ResultsRecorder.Folder);
    }

    [Test]
    public void Adb_is_looked_for_in_every_configured_place_and_empty_settings_are_skipped()
    {
        var candidates = AdbLocator.Candidates("/unity/contents", "/sdk", "", null, "adb");

        CollectionAssert.AreEqual(
            new[]
            {
                Path.Combine("/unity/contents", "PlaybackEngines", "AndroidPlayer", "SDK", "platform-tools", "adb"),
                Path.Combine("/sdk", "platform-tools", "adb"),
            },
            candidates);
        Assert.IsEmpty(AdbLocator.Candidates(null, "", " ", null, "adb"));
        Assert.AreEqual("adb.exe", AdbLocator.ExecutableName(true));
        Assert.AreEqual("adb", AdbLocator.ExecutableName(false));
    }
}
