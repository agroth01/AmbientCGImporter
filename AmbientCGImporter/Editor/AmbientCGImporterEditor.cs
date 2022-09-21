using System;
using System.Net;
using System.IO;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO.Compression;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using System.ComponentModel;

namespace MG
{
    /// <summary>
    /// This script was made in order to reduce the amount of steps required for developers to import textures from ambientcg.com into Unity.
    /// Contains a modified version of script to convert textures to unity format found here: 
    /// https://forum.unity.com/threads/free-ambientcg-to-unity-texture-converter-1500-free-pbr-materials.1219455/
    /// 
    /// For any issues, you can contact me here:
    /// Discord: Groth#0604
    /// Email: software.agroth@gmail.com
    /// </summary>
    public class AmbientCGImporterEditor : EditorWindow
    {
        /// <summary>
        /// Used for caching input from gui elements in window
        /// </summary>
        private struct UserInput
        {
            public string textureUrl;
            public int resolutionIndex;
            public bool logging;
        }
        private UserInput m_userInput = new UserInput();

        private const string m_baseUrl = "https://ambientcg.com/get?file="; // The base of any download url for files
        private string[] m_resolutions = new string[] { "1K", "2K", "4K", "8K", "12K", "16K" };

        public string TexureName
        {
            get { return m_userInput.textureUrl.Split('=')[1]; }
        }

        public string FolderPath
        {
            get { return UnityEngine.Application.dataPath + "/AmbientCGImporter/Imported/" + TexureName; }
        }

        public string RelativePath
        {
            get { return "Assets/AmbientCGImporter/Imported/" + TexureName; }
        }

        [MenuItem("Tools/AmbientCG Importer")]
        public static void OpenWindow()
        {
            EditorWindow ew = EditorWindow.GetWindow(typeof(AmbientCGImporterEditor));
            ew.titleContent = new UnityEngine.GUIContent("AmbientCG Importer");
        }

        private void OnGUI()
        {
            // Draw the custom gui elements and get the user input
            UnityEngine.GUILayout.Space(10);
            m_userInput.textureUrl = EditorGUILayout.TextField("Url", m_userInput.textureUrl);
            UnityEngine.GUILayout.Space(5);
            m_userInput.resolutionIndex = EditorGUILayout.Popup("Resolution", m_userInput.resolutionIndex, m_resolutions);
            UnityEngine.GUILayout.Space(5);
            m_userInput.logging = EditorGUILayout.Toggle("Logging", m_userInput.logging);
            UnityEngine.GUILayout.Space(20);
            if (UnityEngine.GUILayout.Button("Import")) Import();
        }

        /// <summary>
        /// Main method for logic of importing
        /// </summary>
        private void Import()
        {
            // Create the import folder if not already created
            if (!AssetDatabase.IsValidFolder("Assets/AmbientCGImporter/Imported"))
            {
                AssetDatabase.CreateFolder("Assets/AmbientCGImporter", "Imported");
            }

            string url = CreateDownloadLink();
            DownloadFile(url);
        }

        /// <summary>
        /// Creates a direct download link to the file given the input
        /// </summary>
        /// <returns></returns>
        private string CreateDownloadLink()
        {
            string resolution = m_resolutions[m_userInput.resolutionIndex];
            return m_baseUrl + TexureName + "_" + resolution + "-PNG.zip";
        }

        /// <summary>
        /// Starts downloading the file with the given url
        /// </summary>
        private void DownloadFile(string url)
        {
            using (WebClient client = new WebClient())
            {
                Uri uri = new Uri(url);
                client.DownloadFileTaskAsync(uri, FolderPath + ".zip");
                client.DownloadFileCompleted += new AsyncCompletedEventHandler(OnDownloadComplete);
                if (m_userInput.logging) UnityEngine.Debug.Log("Downloading texure " + TexureName + " from " + url);
            }
        }

        /// <summary>
        /// Called when the file has finished downloading
        /// </summary>
        private void OnDownloadComplete(object sender, AsyncCompletedEventArgs e)
        {
            if (m_userInput.logging) UnityEngine.Debug.Log("Download complete. Extracting textures");

            // Create new folder to store the files about to be created
            if (!AssetDatabase.IsValidFolder(FolderPath))
            {
                AssetDatabase.CreateFolder("Assets/AmbientCGImporter/Imported", TexureName);
            }

            // Extract textures from file downloaded
            ExtractAmbientCG
            (
                new ZipArchive(File.OpenRead(FolderPath + ".zip"), ZipArchiveMode.Read),
                FolderPath,
                TexureName
            );

            // Delete the zip file
            File.Delete(FolderPath + ".zip");

            CreateMaterial();
        }

        /// <summary>
        /// Creates a new material from the textures extracted and places it in the folder created
        /// </summary>
        private void CreateMaterial()
        {
            AssetDatabase.Refresh();

            // Create new material
            UnityEngine.Material material = new UnityEngine.Material(UnityEngine.Shader.Find("Standard"));
            material.name = TexureName;

            // Set the textures to the material
            material.EnableKeyword("_NORMALMAP");
            material.EnableKeyword("_METALLICGLOSSMAP");

            // Reimport the normal map as a normal map texture type to avoid annoying error
            TextureImporter normalMapImporter = (TextureImporter)TextureImporter.GetAtPath(RelativePath + "/" + TexureName + "_nml.png");
            normalMapImporter.textureType = TextureImporterType.NormalMap;
            normalMapImporter.SaveAndReimport();

            // Set the textures to the material
            material.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(RelativePath + "/" + TexureName + "_alb.png"));
            material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(RelativePath + "/" + TexureName + "_nml.png"));
            material.SetTexture("_MetallicGlossMap", AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(RelativePath + "/" + TexureName + "_mos.png"));
            material.SetTexture("_ParallaxMap", AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(RelativePath + "/" + TexureName + "_plx.png"));

            // Save the material
            AssetDatabase.CreateAsset(material, RelativePath + "/" + TexureName + ".mat");
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            if (m_userInput.logging) UnityEngine.Debug.Log("Material successfully created at " + RelativePath + "/" + TexureName + ".mat");
        }
        #region Imported Methods
        /// <summary>
        /// Slightly modified code from https://forum.unity.com/threads/free-ambientcg-to-unity-texture-converter-1500-free-pbr-materials.1219455/ in order to fit need of program.
        /// All credits goes to original creator.
        /// </summary>
        private void ExtractAmbientCG(ZipArchive arc, string dir, string name)
        {
            static bool tryFindEntry(ZipArchive arc, string suffix, out ZipArchiveEntry e)
            {
                e = arc.Entries.FirstOrDefault(x => x.Name.ToLowerInvariant().EndsWith(suffix)); return e != null;
            }

            static ZipArchiveEntry findEntryOrNull(ZipArchive arc, string suffix)
            {
                tryFindEntry(arc, suffix, out ZipArchiveEntry e); return e;
            }

            static void copyEntry(ZipArchive arc, string suffix, string outFile, bool throwIfNotFound)
            {
                if (File.Exists(outFile)) File.Delete(outFile);
                if (tryFindEntry(arc, suffix, out ZipArchiveEntry e))
                {
                    using Stream IN = e.Open();
                    using Stream OUT = File.OpenWrite(outFile);
                    IN.CopyTo(OUT);
                }
                else if (throwIfNotFound)
                    throw new Exception($"Could not find an entry ending with {suffix} in [{string.Join(", ", arc.Entries.Select(x => x.Name))}]");
            }

            static byte[] readStreamBytes(ZipArchiveEntry e)
            {
                if (e == null) return null;
                using Stream es = e.Open();
                using MemoryStream ms = new();
                es.CopyTo(ms);
                return ms.ToArray();
            }

            string colorOut = $"{dir}/{name}_alb.png";
            string mosOut = $"{dir}/{name}_mos.png";
            string normalOut = $"{dir}/{name}_nml.png";
            string plxOut = $"{dir}/{name}_plx.png";
            ZipArchiveEntry metalness = findEntryOrNull(arc, "_metalness.png");
            ZipArchiveEntry roughness = findEntryOrNull(arc, "_roughness.png");
            ZipArchiveEntry ao = findEntryOrNull(arc, "_ambientocclusion.png");
            if (File.Exists(mosOut)) File.Delete(mosOut);
            makeMosMap(readStreamBytes(metalness), readStreamBytes(roughness), readStreamBytes(ao), mosOut);
            copyEntry(arc, "_color.png", colorOut, true);
            copyEntry(arc, "_normalgl.png", normalOut, true);
            copyEntry(arc, "_displacement.png", plxOut, false);

            // close zip file after use
            arc.Dispose();
        }

        /// <summary>
        /// Function taken directly from the AmbientCGToUnity.cs file. All credit goes to original creator
        /// </summary>
        private void makeMosMap(byte[] metalBytes, byte[] roughBytes, byte[] aoBytes, string outFile)
        {
            static Bitmap bytesToBitmap(byte[] bytes)
            {
                if (bytes == null) return null;
                using MemoryStream ms = new(bytes);
                return new Bitmap(ms, false);
            }

            // https://stackoverflow.com/questions/1922040/how-to-resize-an-image-c-sharp
            static Bitmap resize(Bitmap b, int w, int h)
            {
                Rectangle destRect = new(0, 0, w, h);
                Bitmap destImage = new(w, h);
                using var graphics = Graphics.FromImage(destImage);
                graphics.CompositingMode = CompositingMode.SourceCopy;
                graphics.CompositingQuality = CompositingQuality.HighQuality;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.SmoothingMode = SmoothingMode.HighQuality;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                using ImageAttributes wrapMode = new();
                wrapMode.SetWrapMode(WrapMode.TileFlipXY);
                graphics.DrawImage(b, destRect, 0, 0, b.Width, b.Height, GraphicsUnit.Pixel, wrapMode);
                return destImage;
            }

            static void matchSizes(ref Bitmap a, ref Bitmap b, ref Bitmap c, out int w, out int h)
            {
                w = 0; h = 0;
                if (a != null) { w = Math.Max(w, a.Width); h = Math.Max(h, a.Height); }
                if (b != null) { w = Math.Max(w, b.Width); h = Math.Max(h, b.Height); }
                if (c != null) { w = Math.Max(w, c.Width); h = Math.Max(h, c.Height); }
                if (a != null && (a.Width != w || a.Height != h)) { Bitmap n = resize(a, w, h); a.Dispose(); a = n; }
                if (b != null && (b.Width != w || b.Height != h)) { Bitmap n = resize(b, w, h); b.Dispose(); b = n; }
                if (c != null && (c.Width != w || c.Height != h)) { Bitmap n = resize(c, w, h); c.Dispose(); c = n; }
            }

            // TODO this is MUCH slower than it needs to be; see LockPixels()
            static Color[] readColors(Bitmap bmp, int w, int h)
            {
                Color[] a = new Color[w * h];
                for (int y = 0; y < h; ++y)
                    for (int x = 0; x < w; ++x)
                        a[(y * w) + x] = bmp.GetPixel(x, y);
                return a;
            }

            // TODO this is MUCH slower than it needs to be; see LockPixels()
            static void writeColors(Bitmap bmp, int w, int h, Color[] a)
            {
                for (int y = 0; y < h; ++y)
                    for (int x = 0; x < w; ++x)
                        bmp.SetPixel(x, y, a[(y * w) + x]);
            }

            static Color[] fakeColors(int len, int b)
            {
                Color[] a = new Color[len];
                for (int i = 0; i < len; ++i)
                    a[i] = Color.FromArgb(255, b, b, b);
                return a;
            }

            static Color[] combineMosColors(Color[] metal, Color[] rough, Color[] ao)
            {
                Debug.Assert(metal != null && rough != null && ao != null && metal.Length > 0 && rough.Length == metal.Length && ao.Length == metal.Length);
                int len = metal.Length;
                Color[] mos = new Color[len];
                for (int i = 0; i < len; ++i)
                    mos[i] = combineMosColor(metal[i], rough[i], ao[i]);
                return mos;
            }

            // this is the unity mask map format -- red is metallic, green is AO, and alpha is smoothness (just inverted
            // roughness). In HDRP and Better Lit, the Blue channel is used for detail mask. Currently, we are not using
            // the detail mask at all, and could possibly repurpose this for parallax or something, but for now I'm leaving
            // it just as 0 in case some materials need detial masks someday and to keep compatibility with other shaders
            // Note that Better Lit uses albedo alpha for parallax and URP default needs a separate parallax map, so there's no
            // real standard for where parallax should be.
            static Color combineMosColor(Color metal, Color rough, Color ao) =>
                Color.FromArgb(
                    red: metal.R,
                    green: ao.R,
                    blue: 0,
                    alpha: 255 - rough.R);

            Bitmap metalBmp = null, roughBmp = null, aoBmp = null;
            Color[] metalColors, roughColors, aoColors;
            int width, height;
            try
            {
                metalBmp = bytesToBitmap(metalBytes);
                roughBmp = bytesToBitmap(roughBytes);
                aoBmp = bytesToBitmap(aoBytes);
                matchSizes(ref metalBmp, ref roughBmp, ref aoBmp, out width, out height);
                metalColors = metalBmp != null ? readColors(metalBmp, width, height) : fakeColors(width * height, 0);
                roughColors = roughBmp != null ? readColors(roughBmp, width, height) : fakeColors(width * height, 127);
                aoColors = aoBmp != null ? readColors(aoBmp, width, height) : fakeColors(width * height, 255);
            }
            finally
            {
                metalBmp?.Dispose();
                roughBmp?.Dispose();
                aoBmp?.Dispose();
            }

            using Bitmap mosBmp = new(width, height);
            writeColors(mosBmp, width, height, combineMosColors(metalColors, roughColors, aoColors));
            mosBmp.Save(outFile);
        }
        #endregion
    }

}