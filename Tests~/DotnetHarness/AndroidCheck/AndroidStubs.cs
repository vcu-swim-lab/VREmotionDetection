namespace UnityEngine.Android
{
    public static class Permission
    {
        public const string Microphone = "android.permission.RECORD_AUDIO";

        public static bool HasUserAuthorizedPermission(string permission) => true;

        public static void RequestUserPermission(string permission) { }
    }
}
