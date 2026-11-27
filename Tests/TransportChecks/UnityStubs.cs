// Minimal host for testing the actual transport without loading Unity; not a substitute for Unity/APK compilation.
namespace UnityEngine
{
    public class MonoBehaviour { }
    public class DisallowMultipleComponent : System.Attribute { }
    public class SerializeField : System.Attribute { }
    public static class Time { public static float unscaledTime => System.Environment.TickCount64 / 1000f; }
    public static class Debug { public static void Log(object value, object context) => System.Console.WriteLine(value); }
}
