using System.IO;
using Android.Content;
using Android.Content.Res;

namespace Prowl.AndroidRunner
{
    public static class AssetExtractor
    {
        public static string EnsureAssetsExtracted(Context context)
        {
            string targetDir = Path.Combine(context.FilesDir!.AbsolutePath, "ProwlData");

            // Gumawa ng extraction indicator para hindi ulitin bawat restart
            string markerFile = Path.Combine(targetDir, ".extracted_marker");
            if (File.Exists(markerFile))
                return targetDir;

            if (Directory.Exists(targetDir))
                Directory.Delete(targetDir, true);

            Directory.CreateDirectory(targetDir);
            ExtractDirectory(context.Assets!, "", targetDir);

            File.WriteAllText(markerFile, "done");
            return targetDir;
        }

        private static void ExtractDirectory(AssetManager assetManager, string relativePath, string targetDir)
        {
            string[]? list = assetManager.List(relativePath);
            if (list == null || list.Length == 0) return;

            foreach (var item in list)
            {
                // Laktawan ang internal Android metadata
                if (item == "webkit" || item == "images" || item == "sounds") continue;

                string subPath = string.IsNullOrEmpty(relativePath) ? item : $"{relativePath}/{item}";
                string destPath = Path.Combine(targetDir, subPath);

                string[]? subList = assetManager.List(subPath);
                if (subList != null && subList.Length > 0)
                {
                    Directory.CreateDirectory(destPath);
                    ExtractDirectory(assetManager, subPath, targetDir);
                }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                    using var inputStream = assetManager.Open(subPath);
                    using var outputStream = File.Create(destPath);
                    inputStream.CopyTo(outputStream);
                }
            }
        }
    }
}
