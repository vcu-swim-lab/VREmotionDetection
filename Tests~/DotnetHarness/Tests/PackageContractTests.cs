using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using VcuSwimLab.Backends;
using VcuSwimLab.Backends.Jev;
using VcuSwimLab.Backends.SparseVr;
using VcuSwimLab.Calibration;
using VcuSwimLab.VREmotionDetection;

// 3.0.0 moved every public type of the runtime and editor assemblies into VcuSwimLab.VREmotionDetection
// (see Documentation~/Migration.md) and renamed UseSparseVr to UseLocalModel (UseSparseVr stays as an alias).
public class PackageContractTests
{
    [Test]
    public void Every_public_type_of_the_runtime_and_editor_assemblies_is_in_the_package_namespace()
    {
        foreach (var assembly in new[] { typeof(NewFaceAuModel).Assembly, typeof(AdbOutput).Assembly })
        {
            var outside = assembly.GetExportedTypes().Where(t => t.Namespace != "VcuSwimLab.VREmotionDetection").ToList();

            CollectionAssert.IsEmpty(outside.Select(t => t.FullName), assembly.GetName().Name);
        }
    }

    [Test]
    public void The_backend_and_calibration_assemblies_keep_their_own_namespaces()
    {
        var expected = new (Assembly assembly, string prefix)[]
        {
            (typeof(IEmotionBackend).Assembly, "VcuSwimLab.Backends"),
            (typeof(JevBackend).Assembly, "VcuSwimLab.Backends.Jev"),
            (typeof(SparseVrBackend).Assembly, "VcuSwimLab.Backends.SparseVr"),
            (typeof(MovementCalibrator).Assembly, "VcuSwimLab.Calibration"),
        };

        foreach (var (assembly, prefix) in expected)
        {
            var outside = assembly.GetExportedTypes().Where(t => t.Namespace != prefix).ToList();

            CollectionAssert.IsEmpty(outside.Select(t => t.FullName), assembly.GetName().Name);
        }
    }

    [Test]
    public void UseSparseVr_is_a_warning_level_obsolete_alias_so_existing_projects_keep_compiling()
    {
        var old = typeof(NewFaceAuModel).GetMethod(nameof(NewFaceAuModel.UseSparseVr)).GetCustomAttribute<ObsoleteAttribute>();

        Assert.IsNotNull(old);
        Assert.IsFalse(old.IsError);
        StringAssert.Contains("UseLocalModel", old.Message);
        Assert.IsNull(typeof(NewFaceAuModel).GetMethod(nameof(NewFaceAuModel.UseLocalModel)).GetCustomAttribute<ObsoleteAttribute>());
    }

    [Test]
    public async Task UseLocalModel_and_the_old_UseSparseVr_select_the_same_backend()
    {
        string viaNew;
        using (var rig = new HarnessRig(modelName: "alias-model"))
        {
            rig.Model.UseLocalModel();
            viaNew = rig.Model.ActiveBackendId;
        }

        using (var rig = new HarnessRig(modelName: "alias-model"))
        {
            rig.Model.UseSparseVr();
            rig.Show(0);
            await rig.Settle();

            Assert.AreEqual(viaNew, rig.Model.ActiveBackendId);
            Assert.AreEqual("alias-model", rig.Model.ActiveBackendId, "the backend id is still the model asset's name, so saved calibrations match");
            Assert.IsTrue((await rig.Model.TryPredict(saveResult: false)).Ok);
        }
    }

    // The model keeps this GUID when its file is renamed, so scenes and prefabs that reference the asset keep finding it.
    private const string ModelGuid = "c8c2538830cca7d42b35813f72fa93bf";

    private static string PackageRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "package.json")) && Directory.Exists(Path.Combine(dir.FullName, "Runtime")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("package root not found above " + AppContext.BaseDirectory);
    }

    [Test]
    public void The_default_model_path_names_the_one_bundled_model_and_its_meta_keeps_the_guid()
    {
        const string packagePrefix = "Packages/com.vcu-swim-lab.vr-emotion-detection/";
        Assert.IsTrue(NewFaceAuModel.DefaultModelPath.StartsWith(packagePrefix));
        string model = Path.Combine(PackageRoot(), NewFaceAuModel.DefaultModelPath.Substring(packagePrefix.Length));

        Assert.IsTrue(File.Exists(model), "DefaultModelPath must point at a file in the package: " + model);
        Assert.IsTrue(File.Exists(model + ".meta"), "Unity needs the .meta next to the model");
        StringAssert.Contains("guid: " + ModelGuid, File.ReadAllText(model + ".meta"));

        string folder = Path.GetDirectoryName(model);
        CollectionAssert.AreEqual(new[] { Path.GetFileName(model) },
                                  Directory.GetFiles(folder, "*.onnx").Select(Path.GetFileName).ToArray(),
                                  "the package ships exactly one model");
    }

    [Test]
    public void UseLocalModel_needs_a_model_asset()
    {
        using var rig = new HarnessRig(withModel: false);

        Assert.Throws<InvalidOperationException>(() => rig.Model.UseLocalModel());
    }

    [Test]
    public async Task Choosing_the_local_model_again_after_a_backend_switch_gives_the_same_working_backend()
    {
        using var rig = new HarnessRig();
        rig.Show(0);
        await rig.Settle();
        string local = rig.Model.ActiveBackendId;

        rig.Model.UseBackend(new JevBackend(new JevOptions("k"), FaceAuNames.Movement, new NeverCalledTransport()));
        Assert.AreNotEqual(local, rig.Model.ActiveBackendId);

        rig.Model.UseLocalModel();

        Assert.AreEqual(local, rig.Model.ActiveBackendId);
        Assert.IsTrue((await rig.Model.TryPredict(saveResult: false)).Ok);
    }

    private sealed class NeverCalledTransport : IHttpTransport
    {
        public Awaitable<HttpReply> PostJsonAsync(string url, string bearerToken, string jsonBody, float timeoutSeconds) =>
            throw new InvalidOperationException("not expected to be called");
    }
}
