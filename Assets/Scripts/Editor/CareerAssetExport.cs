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
            Directory.CreateDirectory(root+"Icons");Directory.CreateDirectory(root+"Reforged/Effects");
            foreach(var s in CareerCatalog.All)
            {
                Write(CareerPaintArt.Icon(s),root+"Icons/"+s.Icon+".png",true,64);
                if(s.kind==CareerSkillKind.Passive)continue;
                var sheet=new PixelCanvas(CareerRaster.Size*12,CareerRaster.Size);for(int i=0;i<12;i++)sheet.Blit(CareerArt.Effect(s,i),i*CareerRaster.Size,0);
                Write(sheet,root+"Reforged/Effects/"+s.id+".png",false,CareerRaster.Ppu);
                Directory.CreateDirectory(root+"Reforged/Layered");
                foreach(bool back in new[]{true,false}){
                    var layer=new PixelCanvas(CareerRaster.Size*12,CareerRaster.Size);for(int i=0;i<12;i++)layer.Blit(CareerVfxArt.Plane(CareerArt.Effect(s,i),back),i*CareerRaster.Size,0);
                    Write(layer,root+"Reforged/Layered/"+s.id+(back?"_back":"_front")+".png",false,CareerRaster.Ppu);
                }
            }
            Directory.CreateDirectory(root+"Reforged/Details");
            foreach(Career c in new[]{Career.Fighter,Career.Guardian,Career.Arcanist,Career.Bishop})foreach(bool charge in new[]{true,false}){
                var sheet=new PixelCanvas(CareerRaster.Size*12,CareerRaster.Size);for(int i=0;i<12;i++)sheet.Blit(CareerVfxArt.Detail(c,charge,i),CareerRaster.Size*i,0);
                Write(sheet,root+"Reforged/Details/"+c+(charge?"_charge":"_hit")+".png",false,CareerRaster.Ppu);
            }
            Directory.CreateDirectory(root+"Reforged/Presentation");
            foreach(var key in CareerVfxArt.VisualKeys){var sheet=new PixelCanvas(CareerRaster.Size*12,CareerRaster.Size);for(int i=0;i<12;i++)sheet.Blit(CareerVfxArt.Visual(key,i),CareerRaster.Size*i,0);Write(sheet,root+"Reforged/Presentation/"+key+".png",false,CareerRaster.Ppu);}
            AssetDatabase.Refresh();Debug.Log("Career art: 36 icons + 28 effect sheets exported.");
        }
        static void Write(PixelCanvas p,string path,bool sprite,int ppu)
        {
            var t=new Texture2D(p.Width,p.Height,TextureFormat.RGBA32,false);t.SetPixels32(p.ToTexturePixels());t.Apply();File.WriteAllBytes(path,t.EncodeToPNG());UnityEngine.Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(path);var importer=(TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType=sprite?TextureImporterType.Sprite:TextureImporterType.Default;
            importer.spriteImportMode=SpriteImportMode.Single;importer.spritePixelsPerUnit=ppu;importer.mipmapEnabled=false;
            importer.filterMode=FilterMode.Point;importer.textureCompression=TextureImporterCompression.Uncompressed;
            importer.maxTextureSize=4096;importer.npotScale=TextureImporterNPOTScale.None;importer.alphaIsTransparency=true;importer.wrapMode=TextureWrapMode.Clamp;importer.SaveAndReimport();
        }
    }
}
