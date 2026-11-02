namespace ShotInk
{
    internal static class BuildFeatures
    {
#if SHOTINK_LITE
        internal const bool Ocr = false;
        internal const string Flavor = "lite";
        internal const string Archive = "ShotInk-portable-lite.zip";
        internal const string Index = "latest-lite.json";
        internal const string Title = "ShotInk 轻量版";
#else
        internal const bool Ocr = true;
        internal const string Flavor = "ocr";
        internal const string Archive = "ShotInk-portable.zip";
        internal const string Index = "latest.json";
        internal const string Title = "ShotInk";
#endif
    }
}
