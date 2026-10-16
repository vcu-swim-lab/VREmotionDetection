using NUnit.Framework;

public class FaceAuNamesTests
{
    private enum Ordered
    {
        Invalid = -1,
        First = 0,
        Second = 1,
        Third = 2,
        Max = 3,
    }

    private enum Scrambled
    {
        Second = 1,
        Invalid = -1,
        First = 0,
    }

    [Test]
    public void Names_follow_the_enum_values_and_skip_negative_members()
    {
        CollectionAssert.AreEqual(new[] { "First", "Second", "Third", "Max" }, FaceAuNames.Of(typeof(Ordered)));
        CollectionAssert.AreEqual(new[] { "First", "Second" }, FaceAuNames.Of(typeof(Scrambled)));
    }

    [Test]
    public void The_meta_enum_gives_the_63_face_movements_and_no_tongue_values()
    {
        var names = FaceAuNames.Movement;

        Assert.AreEqual(63, names.Length);
        Assert.AreEqual("BrowLowererL", names[0]);
        Assert.AreEqual("UpperLipRaiserR", names[62]);
        CollectionAssert.DoesNotContain(names, "Invalid");
        CollectionAssert.DoesNotContain(names, "TongueOut");
    }
}
