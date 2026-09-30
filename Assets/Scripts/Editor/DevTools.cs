using System.IO;
using UnityEditor;
using UnityEngine;

namespace DotRPG.EditorTools
{
    /// <summary>Developer conveniences: save data management and exporting placeholder art for artists.</summary>
    public static class DevTools
    {
        [MenuItem("dotRPG/Save Data/Open Save Folder", priority = 40)]
        public static void OpenSaveFolder()
        {
            Directory.CreateDirectory(Application.persistentDataPath);
            EditorUtility.RevealInFinder(Application.persistentDataPath);
        }

        [MenuItem("dotRPG/Save Data/Delete Save Slot", priority = 41)]
        public static void DeleteSave()
        {
            if (!EditorUtility.DisplayDialog("dotRPG", "저장 슬롯을 삭제할까요? (설정 파일은 유지됩니다)", "삭제", "취소")) return;
            new SaveSystem().Delete();
            Debug.Log("[dotRPG] Save slot deleted.");
        }

        [MenuItem("dotRPG/Save Data/Delete Settings", priority = 42)]
        public static void DeleteSettings()
        {
            if (File.Exists(SettingsManager.FilePath)) File.Delete(SettingsManager.FilePath);
            Debug.Log("[dotRPG] Settings file deleted.");
        }

        /// <summary>
        /// Writes every placeholder sprite to Assets/ArtExport as PNG. Artists can paint over these
        /// (same size & pivot) and move the result to Assets/Resources/Art/ to replace the placeholder.
        /// </summary>
        [MenuItem("dotRPG/Art/Export Placeholder Sprites", priority = 60)]
        public static void ExportPlaceholderArt()
        {
            const string folder = "Assets/ArtExport";
            Directory.CreateDirectory(folder);
            int count = 0;
            foreach (var key in ProceduralArt.AllKeys())
            {
                var canvas = ProceduralArt.Draw(key);
                if (canvas != null && Write(folder, key, canvas)) count++;
            }
            foreach (var look in new[] { CharacterLook.Player, CharacterLook.Mage, CharacterLook.Skeleton })
                foreach (var dir in new[] { "down", "up", "side" })
                    foreach (var frame in ProceduralArt.CharacterFrames)
                        if (Write(folder, $"char_{look.id}_{dir}_{frame}", ProceduralArt.DrawCharacter(look, dir, frame))) count++;
            AssetDatabase.Refresh();
            Debug.Log($"[dotRPG] Exported {count} placeholder sprites to {folder}.");
        }

        static bool Write(string folder, string key, PixelCanvas canvas)
        {
            var tex = new Texture2D(canvas.Width, canvas.Height, TextureFormat.RGBA32, false);
            tex.SetPixels32(canvas.ToTexturePixels());
            tex.Apply();
            File.WriteAllBytes(Path.Combine(folder, key + ".png"), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            return true;
        }
    }
}
