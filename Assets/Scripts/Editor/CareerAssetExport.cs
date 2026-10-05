using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace DotRPG.EditorTools
{
    public static class CareerAssetExport
    {
        [Serializable] public sealed class Catalog { public int schema=1;public CareerSkill[] skills=CareerCatalog.All; }
        public static string CatalogJson()=>JsonUtility.ToJson(new Catalog());
        [MenuItem("dotRPG/Export Career Art")]
        public static void Export()
        {
            const string root="Assets/Resources/Art/Careers/";
            Directory.CreateDirectory(root+"Icons");Directory.CreateDirectory(root+"Effects");
            foreach(var s in CareerCatalog.All)
            {
                Write(CareerArt.Icon(s),root+"Icons/"+s.Icon+".png",true,32);
                if(s.kind==CareerSkillKind.Passive)continue;
                var sheet=new PixelCanvas(1152,96);for(int i=0;i<12;i++)sheet.Blit(CareerArt.Effect(s,i),i*96,0);
                Write(sheet,root+"Effects/"+s.id+".png",false,48);
            }
            Directory.CreateDirectory(root+"Details");
            foreach(Career c in new[]{Career.Fighter,Career.Guardian,Career.Arcanist,Career.Bishop})foreach(bool charge in new[]{true,false}){
                var sheet=new PixelCanvas(1152,96);for(int i=0;i<12;i++)sheet.Blit(CareerVfxArt.Detail(c,charge,i),96*i,0);
                Write(sheet,root+"Details/"+c+(charge?"_charge":"_hit")+".png",false,48);
            }
            AssetDatabase.Refresh();Debug.Log("Career art: 36 icons + 28 effect sheets exported.");
        }
        static void Write(PixelCanvas p,string path,bool sprite,int ppu)
        {
            var t=new Texture2D(p.Width,p.Height,TextureFormat.RGBA32,false);t.SetPixels32(p.ToTexturePixels());t.Apply();File.WriteAllBytes(path,t.EncodeToPNG());UnityEngine.Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(path);var importer=(TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType=sprite?TextureImporterType.Sprite:TextureImporterType.Default;
            importer.spriteImportMode=SpriteImportMode.Single;importer.spritePixelsPerUnit=ppu;importer.mipmapEnabled=false;
            importer.filterMode=FilterMode.Point;importer.textureCompression=TextureImporterCompression.Uncompressed;
            importer.npotScale=TextureImporterNPOTScale.None;importer.alphaIsTransparency=true;importer.wrapMode=TextureWrapMode.Clamp;importer.SaveAndReimport();
        }
    }
}
