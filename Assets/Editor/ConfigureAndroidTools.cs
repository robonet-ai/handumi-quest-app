using System;
using System.IO;
using System.Linq;
using UnityEditor;

public static class ConfigureAndroidTools
{
    public static string ResolvedSdkPath { get; private set; }
    public static string ResolvedNdkPath { get; private set; }
    public static string ResolvedJdkPath { get; private set; }

    public static void Apply()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        ResolvedSdkPath = FirstDirectory(
            Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT"),
            Environment.GetEnvironmentVariable("ANDROID_HOME"),
            Path.Combine(home, "Android", "Sdk"));
        if (ResolvedSdkPath == null)
            throw new DirectoryNotFoundException(
                "Android SDK not found. Set ANDROID_SDK_ROOT or ANDROID_HOME.");

        ResolvedNdkPath = FirstDirectory(
            Environment.GetEnvironmentVariable("ANDROID_NDK_ROOT"),
            Environment.GetEnvironmentVariable("ANDROID_NDK_HOME"),
            Path.Combine(ResolvedSdkPath, "ndk", "27.2.12479018"),
            NewestChildDirectory(Path.Combine(ResolvedSdkPath, "ndk")));
        if (ResolvedNdkPath == null)
            throw new DirectoryNotFoundException(
                "Android NDK not found. Set ANDROID_NDK_ROOT or install it with sdkmanager.");

        ResolvedJdkPath = FirstDirectory(
            Environment.GetEnvironmentVariable("JAVA_HOME"),
            Path.Combine(home, ".jdks", "corretto-17.0.16"));
        if (ResolvedJdkPath == null)
            throw new DirectoryNotFoundException(
                "JDK not found. Set JAVA_HOME to a Unity-compatible JDK 17.");

        SetPath("sdkRootPath", "AndroidSdkRoot", ResolvedSdkPath);
        SetPath("ndkRootPath", "AndroidNdkRoot", ResolvedNdkPath);
        SetPath("jdkRootPath", "JdkPath", ResolvedJdkPath);
    }

    private static void SetPath(string propertyName, string fallbackPreference, string path)
    {
        var toolsType = Type.GetType("UnityEditor.Android.AndroidExternalToolsSettings, UnityEditor.Android.Extensions");
        var property = toolsType?.GetProperty(propertyName);
        if (property != null)
        {
            property.SetValue(null, path);
            return;
        }

        EditorPrefs.SetString(fallbackPreference, path);
    }

    private static string FirstDirectory(params string[] candidates)
    {
        return candidates.FirstOrDefault(path =>
            !string.IsNullOrWhiteSpace(path) && Directory.Exists(path));
    }

    private static string NewestChildDirectory(string parent)
    {
        if (!Directory.Exists(parent))
            return null;

        return Directory.GetDirectories(parent)
            .OrderByDescending(path => path, StringComparer.Ordinal)
            .FirstOrDefault();
    }
}
