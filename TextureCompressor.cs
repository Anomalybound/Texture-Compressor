#if UNITY_EDITOR
using System;
using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using Debug = UnityEngine.Debug;

public static class TextureCompressor
{
    private const string Version = "1.1.1";

    private const int JPGQualityLevel = 100;

    private static readonly Dictionary<Texture2D, List<Material>> Sources = new Dictionary<Texture2D, List<Material>>();

    private static readonly string[] Filters =
    {
        ".jpg", ".jpeg"
    };

    [MenuItem("Assets/TextureCompressor/To PNG", true)]
    [MenuItem("Assets/TextureCompressor/To JPG", true)]
    [MenuItem("Assets/TextureCompressor/To PNG (Delete Old)", true)]
    [MenuItem("Assets/TextureCompressor/To JPG (Delete Old)", true)]
    [MenuItem("Assets/TextureCompressor/Auto Encode Texture", true)]
    [MenuItem("Assets/TextureCompressor/Auto Encode Texture(Delete Old)", true)]
    public static bool ValidateEncodeTetxure()
    {
        return Selection.activeObject != null &&
               Selection.activeObject is Texture2D;
    }

    [MenuItem("Assets/TextureCompressor/To PNG")]
    public static void EncodeTetxureToPNGKeepOld()
    {
        AutoEncodeTexture(false, tex => true);
    }

    [MenuItem("Assets/TextureCompressor/To JPG")]
    public static void EncodeTetxureToJPGKeepOld()
    {
        AutoEncodeTexture(false, tex => false);
    }

    [MenuItem("Assets/TextureCompressor/To PNG (Delete Old)")]
    public static void EncodeTetxureToPNG()
    {
        AutoEncodeTexture(true, tex => true);
    }

    [MenuItem("Assets/TextureCompressor/To JPG (Delete Old)")]
    public static void EncodeTetxureToJPG()
    {
        AutoEncodeTexture(true, tex => false);
    }

    [MenuItem("Assets/TextureCompressor/Auto Encode Texture")]
    public static void AutoEncodeTexture()
    {
        AutoEncodeTexture(false, TextureHasAlpha);
    }

    [MenuItem("Assets/TextureCompressor/Auto Encode Texture(Delete Old)")]
    public static void AutoEncodeTextureAndDeleteOld()
    {
        AutoEncodeTexture(true, TextureHasAlpha);
    }

    private static bool TextureHasAlpha(Texture2D tex2D)
    {
        var path = AssetDatabase.GetAssetPath(tex2D);
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer != null)
        {
            var hasAlpha = importer.DoesSourceTextureHaveAlpha();
            if (hasAlpha)
            {
                Debug.Log(tex2D + " - has alpha channel");
            }

            return hasAlpha;
        }

        Debug.LogError("Texture is not exists :" + tex2D);
        return true;
    }

    private static void AutoEncodeTexture(bool deleteOld, Predicate<Texture2D> isPNG)
    {
        try
        {
            CacheAllMaterialInProject();
            var length = Selection.objects.Length;
            var selecteds = Selection.objects.OrderBy(x => x.name).ToList();
            for (var index = length - 1; index >= 0; index--)
            {
                var Obj = selecteds.ElementAt(index);
                if (Obj == null || !(Obj is Texture2D)) continue;
                var assetExtension = Path.GetExtension(AssetDatabase.GetAssetPath(Obj));

                EditorUtility.DisplayProgressBar("Encoding Textures",
                    string.Format("Encoding : {0} ({1}/{2})", Obj.name, length - index, length),
                    (length - index) / (float) length);

                var tex2D = Obj as Texture2D;
                EncodeSingle(Obj as Texture2D, deleteOld, isPNG(tex2D));
            }

            EditorUtility.ClearProgressBar();
            Sources.Clear();
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            EditorUtility.ClearProgressBar();
            throw;
        }
    }

    private static void CacheAllMaterialInProject()
    {
        var guids = AssetDatabase.FindAssets("t:material");
        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            var textures = FindAllTexture2Ds(material);
            foreach (var texture in textures)
            {
                if (Sources.ContainsKey(texture))
                {
                    Sources[texture].Add(material);
                }
                else
                {
                    Sources.Add(texture, new List<Material> {material});
                }
            }
        }
    }

    private static List<Texture2D> FindAllTexture2Ds(Material material)
    {
        var allTexture = new List<Texture2D>();
        var shader = material.shader;
        for (var i = 0; i < ShaderUtil.GetPropertyCount(shader); i++)
        {
            if (ShaderUtil.GetPropertyType(shader, i) != ShaderUtil.ShaderPropertyType.TexEnv) continue;
            var texture = material.GetTexture(ShaderUtil.GetPropertyName(shader, i)) as Texture2D;
            if (texture != null)
            {
                allTexture.Add(texture);
            }
        }

        return allTexture;
    }

    private static void ReplaceDependcies(Texture2D original, Texture2D newTexture, Material material)
    {
        var shader = material.shader;
        for (var i = 0; i < ShaderUtil.GetPropertyCount(shader); i++)
        {
            if (ShaderUtil.GetPropertyType(shader, i) != ShaderUtil.ShaderPropertyType.TexEnv) continue;
            var texture = material.GetTexture(ShaderUtil.GetPropertyName(shader, i)) as Texture2D;
            if (texture == original)
            {
                material.SetTexture(ShaderUtil.GetPropertyName(shader, i), newTexture);
            }
        }
    }

    public static void EncodeSingle(Texture2D texture, bool deleteOld, bool isPNG)
    {
        Texture2D origTexture = texture;
        var assetPath = AssetDatabase.GetAssetPath(origTexture);
        var importer = (TextureImporter) AssetImporter.GetAtPath(assetPath);

        bool needRevert = false;

        var tis = new TextureImporterSettings();
        importer.ReadTextureSettings(tis);

        var compression = importer.textureCompression;

        var isBipmap = importer.mipmapEnabled;
        var isLinear = importer.sRGBTexture;

        if (!importer.isReadable)
        {
            needRevert = true;
            importer.isReadable = true;
            importer.SaveAndReimport();
            AssetDatabase.Refresh();
        }

        var pixels = origTexture.GetPixels();
        var newTxt = new Texture2D(origTexture.width, origTexture.height,
            importer.DoesSourceTextureHaveAlpha() ? TextureFormat.ARGB32 : TextureFormat.RGB24, isBipmap, isLinear);

        if (importer.textureType == TextureImporterType.NormalMap)
        {
            DTXnmColors(newTxt, pixels);
        }
        else
        {
            newTxt.SetPixels(pixels);
        }

        byte[] buff = { };

        buff = isPNG ? newTxt.EncodeToPNG() : newTxt.EncodeToJPG(JPGQualityLevel);

        var ext = isPNG ? ".png" : ".jpg";
        var filePath = Path.GetDirectoryName(assetPath) + "/" + origTexture.name +
                       (deleteOld ? ext : "_new" + ext);
        File.WriteAllBytes(filePath, buff);

        if (needRevert)
        {
            importer.isReadable = false;
            importer.SaveAndReimport();
        }

        AssetDatabase.Refresh();

        importer = (TextureImporter) AssetImporter.GetAtPath(filePath);
        if (importer != null)
        {
            importer.SetTextureSettings(tis);
            importer.textureCompression = compression;
            importer.SaveAndReimport();
            AssetDatabase.Refresh();
        }

        var newTexture = AssetDatabase.LoadAssetAtPath<Texture>(filePath);

        var oriCol = EditorGUIUtility.isProSkin ? "#00b300" : "#b300b3";
        var toCol = EditorGUIUtility.isProSkin ? "#fc0" : "#03f";

        if (newTexture == null)
        {
            Debug.Log("Waiting for new texture - " + Path.GetFileName(filePath));
        }

        Debug.Log(string.Format("Encode <color={2}>{0}</color> to: <color={3}>{1}</color>",
                Path.GetFileName(assetPath), Path.GetFileName(filePath), oriCol, toCol),
            newTexture);

        // Replace in dependcies
        if (Sources.ContainsKey(origTexture))
        {
            Debug.LogFormat("We found {0} materials realted to {1}.", Sources[origTexture].Count, origTexture);
            foreach (var material in Sources[origTexture])
            {
                ReplaceDependcies(origTexture, newTexture as Texture2D, material);
            }

            Sources.Remove(origTexture);
            AssetDatabase.Refresh();
        }

        Selection.activeObject = newTexture;
        if (!deleteOld)
        {
            return;
        }

        if (filePath == assetPath)
        {
            return;
        }

        Debug.Log("Deleting @" + assetPath);
        AssetDatabase.DeleteAsset(assetPath);
        AssetDatabase.Refresh();
    }

    private static void DTXnmColors(Texture2D target, Color[] colors)
    {
#if UNITY_IPHONE || UNITY_ANDROID
        target.SetPixels(colors);
        return;
#endif

        for (int i = 0; i < colors.Length; i++)
        {
            Color c = colors[i];
            c.r = c.a * 2 - 1; //red<-alpha (x<-w)
            c.g = c.g * 2 - 1; //green is always the same (y)
            Vector2 xy = new Vector2(c.r, c.g); //this is the xy vector
            c.b = Mathf.Sqrt(1 - Mathf.Clamp01(Vector2.Dot(xy, xy))); //recalculate the blue channel (z)
            colors[i] = new Color(c.r * 0.5f + 0.5f, c.g * 0.5f + 0.5f, c.b * 0.5f + 0.5f); //back to 0-1 range
        }

        target.SetPixels(colors); //apply pixels to the texture
        target.Apply();
    }

    private static Texture2D NormalMap(Texture2D source)
    {
        var normalTexture = new Texture2D(source.width, source.height, TextureFormat.ARGB32, true);
        Color theColour = new Color();
        for (int x = 0; x < source.width; x++)
        {
            for (int y = 0; y < source.height; y++)
            {
                theColour.r = 0;
                theColour.g = source.GetPixel(x, y).g;
                theColour.b = 0;
                theColour.a = source.GetPixel(x, y).r;
                normalTexture.SetPixel(x, y, theColour);
            }
        }

        normalTexture.Apply();
        return normalTexture;
    }
}
#endif
