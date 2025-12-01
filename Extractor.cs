using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AssetStudio;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace VbHelperSpriteExtractor
{
    public class Extractor
    {
        private AssetsManager assetsManager;
        private static readonly Regex DIM_MON_PATTERN = new Regex(@"dim\d+_mon\d+", RegexOptions.Compiled);

        public event Action<int, int> ProgressUpdated;
        public event Action<string> StatusUpdated;
        public event Action<string> LogMessage;

        public Extractor(AssetsManager assetsManager)
        {
            this.assetsManager = assetsManager;
        }

        public void ExtractFromAPK(string apkPath, string outputPath)
        {
            try
            {
                UpdateStatus("Extracting APK...");
                Log("Starting extraction from APK: " + Path.GetFileName(apkPath));

                // Step 1: Extract main APK
                string tempDir = Path.Combine(Path.GetTempPath(), "vba_extract_" + Guid.NewGuid().ToString("N").Substring(0, 8));
                Directory.CreateDirectory(tempDir);

                try
                {
                    ExtractAPK(apkPath, tempDir);

                    // Step 2: Find UnityDataAssetPack.apk
                    string unityAPK = FindUnityDataAssetPack(tempDir);
                    if (unityAPK == null)
                    {
                        throw new Exception("UnityDataAssetPack.apk not found in APK");
                    }

                    Log("Found UnityDataAssetPack.apk");

                    // Step 3: Extract Unity APK
                    string unityExtractDir = Path.Combine(tempDir, "unity_extract");
                    Directory.CreateDirectory(unityExtractDir);
                    ExtractAPK(unityAPK, unityExtractDir);

                    // Step 4: Find bundle and asset files
                    string assetsDir = FindAssetsDirectory(unityExtractDir);
                    if (assetsDir == null)
                    {
                        assetsDir = unityExtractDir;
                        Log("Using unity extract directory as base: " + assetsDir);
                    }
                    else
                    {
                        Log("Found assets directory: " + assetsDir);
                    }

                    // Step 5: Create output directory
                    string dimSpritesDir = Path.Combine(outputPath, "dim_sprites");
                    Directory.CreateDirectory(dimSpritesDir);

                    // Step 6: Load and process assets
                    UpdateStatus("Loading assets...");
                    Log($"Loading assets from: {unityExtractDir}");
                    assetsManager.LoadFolder(unityExtractDir);

                    Log($"Loaded {assetsManager.assetsFileList.Count} asset files");

                    // Step 7: Extract dim sprites
                    UpdateStatus("Extracting dim sprites...");
                    ExtractSpriteAtlases(dimSpritesDir);

                    Log("Extraction completed successfully!");
                    UpdateStatus("Complete");
                }
                finally
                {
                    // Cleanup temp directory
                    try
                    {
                        Directory.Delete(tempDir, true);
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                Log($"ERROR: {ex.Message}");
                Log($"Stack trace: {ex.StackTrace}");
                throw;
            }
        }

        private void ExtractAPK(string apkPath, string outputDir)
        {
            using (ZipArchive archive = ZipFile.OpenRead(apkPath))
            {
                int total = archive.Entries.Count;
                int current = 0;

                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    string destinationPath = Path.Combine(outputDir, entry.FullName);
                    string directory = Path.GetDirectoryName(destinationPath);

                    if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    if (!string.IsNullOrEmpty(entry.Name))
                    {
                        entry.ExtractToFile(destinationPath, true);
                    }

                    current++;
                    ProgressUpdated?.Invoke(current, total);
                }
            }
        }

        private string FindUnityDataAssetPack(string extractDir)
        {
            string[] apkFiles = Directory.GetFiles(extractDir, "UnityDataAssetPack.apk", SearchOption.AllDirectories);
            if (apkFiles.Length > 0)
            {
                return apkFiles[0];
            }
            return null;
        }

        private string FindAssetsDirectory(string extractDir)
        {
            var bundleFiles = Directory.GetFiles(extractDir, "*.bundle", SearchOption.AllDirectories);
            var assetFiles = Directory.GetFiles(extractDir, "*.assets", SearchOption.AllDirectories);
            
            Log($"Found {bundleFiles.Length} .bundle files and {assetFiles.Length} .assets files");
            
            if (bundleFiles.Length > 0 || assetFiles.Length > 0)
            {
                if (bundleFiles.Length > 0)
                {
                    Log($"Sample bundle file: {bundleFiles[0]}");
                }
                if (assetFiles.Length > 0)
                {
                    Log($"Sample asset file: {assetFiles[0]}");
                }
                return extractDir;
            }
            
            return null;
        }

        private void ExtractSpriteAtlases(string dimSpritesDir)
        {
            var spriteAtlases = new List<SpriteAtlas>();

            // Collect all SpriteAtlas objects matching dim_mon pattern
            foreach (var assetsFile in assetsManager.assetsFileList)
            {
                foreach (var obj in assetsFile.Objects)
                {
                    if (obj is SpriteAtlas spriteAtlas)
                    {
                        string atlasName = spriteAtlas.m_Name;
                        if (!string.IsNullOrEmpty(atlasName) && DIM_MON_PATTERN.IsMatch(atlasName))
                        {
                            if (spriteAtlas.m_PackedSprites != null && spriteAtlas.m_PackedSprites.Length == 12)
                            {
                                spriteAtlases.Add(spriteAtlas);
                            }
                        }
                    }
                }
            }

            int total = spriteAtlases.Count;
            int current = 0;
            int extractedCount = 0;

            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount
            };

            var fileLocks = new System.Collections.Concurrent.ConcurrentDictionary<string, object>();

            Parallel.ForEach(spriteAtlases, parallelOptions, spriteAtlas =>
            {
                try
                {
                    string atlasName = spriteAtlas.m_Name;
                    string animDir = Path.Combine(dimSpritesDir, atlasName);
                    Directory.CreateDirectory(animDir);

                    var spriteList = new List<(string name, Image<Bgra32> image)>();

                    foreach (var spritePtr in spriteAtlas.m_PackedSprites)
                    {
                        if (spritePtr.TryGet(out Sprite sprite))
                        {
                            try
                            {
                                var spriteImage = sprite.GetImage();
                                if (spriteImage != null)
                                {
                                    spriteList.Add((sprite.m_Name, spriteImage));
                                }
                            }
                            catch (Exception spriteEx)
                            {
                                if (!spriteEx.Message.Contains("Texture2DDecoder") && !spriteEx.Message.Contains("type initializer"))
                                {
                                    Log($"Error extracting sprite {sprite.m_Name} from atlas {atlasName}: {spriteEx.Message}");
                                }
                            }
                        }
                    }

                    if (spriteList.Count == 12)
                    {
                        // Sort by frame number
                        spriteList.Sort((a, b) =>
                        {
                            if (int.TryParse(a.name, out int aNum) && int.TryParse(b.name, out int bNum))
                                return aNum.CompareTo(bNum);
                            return a.name.CompareTo(b.name);
                        });

                        // Save PNGs with file locking
                        for (int i = 0; i < spriteList.Count; i++)
                        {
                            string filename = $"{atlasName}_{spriteList[i].name}.png";
                            string filepath = Path.Combine(animDir, filename);
                            var lockObj = fileLocks.GetOrAdd(filepath, _ => new object());
                            
                            lock (lockObj)
                            {
                                if (!File.Exists(filepath))
                                {
                                    using (var fileStream = File.Create(filepath))
                                    {
                                        spriteList[i].image.SaveAsPng(fileStream);
                                    }
                                }
                            }
                            spriteList[i].image.Dispose();
                        }

                        Interlocked.Increment(ref extractedCount);
                        Log($"Extracted animation: {atlasName}");
                    }
                }
                catch (Exception ex)
                {
                    if (!ex.Message.Contains("Texture2DDecoder") && !ex.Message.Contains("type initializer"))
                    {
                        Log($"Error extracting sprite atlas {spriteAtlas.m_Name}: {ex.Message}");
                    }
                }

                int progress = Interlocked.Increment(ref current);
                if (progress % 10 == 0 || progress == total)
                {
                    ProgressUpdated?.Invoke(progress, total);
                }
            });

            Log($"Extracted {extractedCount} sprite animations");
        }

        private void UpdateStatus(string status)
        {
            StatusUpdated?.Invoke(status);
        }

        private void Log(string message)
        {
            LogMessage?.Invoke(message);
        }
    }
}
