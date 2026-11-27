using System;
using System.IO;
using System.Linq;
using System.Xml;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Android;
using UnityEngine;

public static class HW3Build
{
    [MenuItem("HW3/Build Quest APK")]
    public static void Build()
    {
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            throw new InvalidOperationException("File > Build Profiles > Android > Switch Platform first.");
        PlayerSettings.Android.forceInternetPermission = true;
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
        Directory.CreateDirectory("Builds");
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
            locationPathName = "Builds/VR_HW3.apk",
            target = BuildTarget.Android,
            options = BuildOptions.None
        });
        if (report.summary.result != BuildResult.Succeeded)
            throw new BuildFailedException("HW3 APK build failed: " + report.summary.result);
        Debug.Log("APK built: " + Path.GetFullPath("Builds/VR_HW3.apk"));
    }
}

// Assert/add INTERNET to the generated Unity library manifest, without replacing XR manifest entries.
public sealed class HW3InternetManifest : IPostGenerateGradleAndroidProject
{
    public int callbackOrder { get { return 100; } }
    public void OnPostGenerateGradleAndroidProject(string path)
    {
        string file = Path.Combine(path, "src/main/AndroidManifest.xml");
        var xml = new XmlDocument();
        xml.Load(file);
        const string ns = "http://schemas.android.com/apk/res/android";
        bool found = false;
        foreach (XmlNode node in xml.GetElementsByTagName("uses-permission"))
            if (node.Attributes["name", ns]?.Value == "android.permission.INTERNET") found = true;
        if (!found)
        {
            var permission = xml.CreateElement("uses-permission");
            var attribute = xml.CreateAttribute("android", "name", ns);
            attribute.Value = "android.permission.INTERNET";
            permission.Attributes.Append(attribute);
            xml.DocumentElement.AppendChild(permission);
            xml.Save(file);
        }
    }
}
