using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using VcuSwimLab.Calibration;

public class CalibrationStoreTests
{
    private string root;
    private CalibrationStore store;

    [SetUp]
    public void SetUp()
    {
        root = Path.Combine(Path.GetTempPath(), "calibration-store-" + Guid.NewGuid().ToString("N"));
        store = new CalibrationStore(root);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, true);
        }
    }

    private static CalibrationProfile Profile()
    {
        var names = Enum.GetNames(typeof(Emotion));
        var scores = new double[names.Length][];
        var movement = new double[names.Length][];
        for (int c = 0; c < names.Length; ++c)
        {
            scores[c] = Enumerable.Range(0, names.Length).Select(i => -1.5 + 0.0123456789 * (c * 7 + i)).ToArray();
            movement[c] = Enumerable.Range(0, CalibrationMath.FaceActionUnitCount).Select(i => 0.001 * (c + 1) * i).ToArray();
        }

        return new CalibrationProfile(MovementCalibrator.ImplementationVersion, names, scores, movement);
    }

    [Test]
    public void A_saved_calibration_loads_back_unchanged()
    {
        var profile = Profile();

        Assert.IsTrue(store.TrySave("player_1", "model-a", profile, out var error), error);
        Assert.IsTrue(store.Exists("player_1"));
        Assert.IsTrue(store.TryLoad("player_1", "model-a", out var loaded, out error), error);

        Assert.AreEqual(profile.ImplementationVersion, loaded.ImplementationVersion);
        CollectionAssert.AreEqual(profile.ClassNames, loaded.ClassNames);
        for (int c = 0; c < profile.ScoreCentroids.Length; ++c)
        {
            CollectionAssert.AreEqual(profile.ScoreCentroids[c], loaded.ScoreCentroids[c], new DoubleTolerance());
            CollectionAssert.AreEqual(profile.MovementCentroids[c], loaded.MovementCentroids[c], new DoubleTolerance());
        }
    }

    [Test]
    public void The_file_keeps_the_layout_other_readers_depend_on()
    {
        store.TrySave("layout", "model-a", Profile(), out _);

        var json = JObject.Parse(File.ReadAllText(store.PathFor("layout")));

        CollectionAssert.AreEqual(
            new[]
            {
                "implementationVersion", "faceModel", "savedUtc", "classNames",
                "scoreWidth", "scoreCentroids", "movementWidth", "movementCentroids",
            },
            json.Properties().Select(p => p.Name).ToArray());
        Assert.AreEqual("model-a", (string)json["faceModel"]);
        Assert.AreEqual(7, (int)json["scoreWidth"]);
        Assert.AreEqual(63, (int)json["movementWidth"]);
        Assert.AreEqual(49, json["scoreCentroids"].Count());
        Assert.AreEqual(441, json["movementCentroids"].Count());
    }

    [Test]
    public void Saving_over_an_existing_file_replaces_it_and_leaves_no_temporary_file()
    {
        store.TrySave("again", "model-a", Profile(), out _);
        Assert.IsTrue(store.TrySave("again", "model-b", Profile(), out var error), error);

        Assert.IsFalse(store.TryLoad("again", "model-a", out _, out _));
        Assert.IsTrue(store.TryLoad("again", "model-b", out _, out _));
        CollectionAssert.IsEmpty(Directory.GetFiles(root, "*.tmp"));
    }

    [Test]
    public void A_calibration_made_for_another_model_is_refused()
    {
        store.TrySave("mismatch", "model-a", Profile(), out _);

        Assert.IsFalse(store.TryLoad("mismatch", "model-b", out var profile, out var error));
        Assert.IsNull(profile);
        Assert.AreEqual("the saved calibration for 'mismatch' was made with model 'model-a', not 'model-b'; calibrate again", error);
    }

    [Test]
    public void A_missing_or_damaged_file_is_reported_not_thrown()
    {
        Assert.IsFalse(store.TryLoad("nobody", "m", out _, out var error));
        Assert.AreEqual("no saved calibration for 'nobody'", error);

        Directory.CreateDirectory(root);
        File.WriteAllText(store.PathFor("broken"), "{ not json");
        Assert.IsFalse(store.TryLoad("broken", "m", out _, out error));
        StringAssert.StartsWith("the saved calibration for 'broken' could not be read", error);
    }

    [TestCase("")]
    [TestCase(null)]
    [TestCase("../escape")]
    [TestCase("a b")]
    [TestCase("a/b")]
    [TestCase("name\n")]
    public void A_user_id_that_is_not_a_plain_name_is_refused_everywhere(string userId)
    {
        Assert.Throws<ArgumentException>(() => store.PathFor(userId));
        Assert.Throws<ArgumentException>(() => store.Exists(userId));
        Assert.Throws<ArgumentException>(() => store.Delete(userId));
        Assert.Throws<ArgumentException>(() => store.TrySave(userId, "m", Profile(), out _));
        Assert.Throws<ArgumentException>(() => store.TryLoad(userId, "m", out _, out _));
        Assert.Throws<ArgumentException>(() => CalibrationStore.DefaultPathFor(userId));
    }

    [Test]
    public void Deleting_removes_the_file_and_is_safe_to_repeat()
    {
        store.TrySave("gone", "m", Profile(), out _);

        store.Delete("gone");
        store.Delete("gone");

        Assert.IsFalse(store.Exists("gone"));
    }

    [Test]
    public void A_folder_that_cannot_be_written_is_reported()
    {
        string blocker = Path.Combine(Path.GetTempPath(), "calibration-blocker-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(blocker, "a file where a folder is needed");
        try
        {
            var blocked = new CalibrationStore(Path.Combine(blocker, "inside"));

            Assert.IsFalse(blocked.TrySave("x", "m", Profile(), out var error));
            StringAssert.Contains("could not be saved", error);
        }
        finally
        {
            File.Delete(blocker);
        }
    }

    private sealed class DoubleTolerance : System.Collections.IComparer
    {
        public int Compare(object x, object y)
        {
            return Math.Abs((double)x - (double)y) < 1e-9 ? 0 : ((double)x).CompareTo((double)y);
        }
    }
}
