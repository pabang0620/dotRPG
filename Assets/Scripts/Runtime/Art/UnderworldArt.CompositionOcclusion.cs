using UnityEngine;

namespace DotRPG
{
    public static partial class UnderworldArt
    {
        // Manually traced from composition-depths.png at its original 1355 x 1161 size.
        // Coordinates below start at the source image's top-left. The same source raster
        // used by the three base layers supplies these pixels; there is no repaint/resample.
        const int ColumnReferenceWidth=1355,ColumnReferenceHeight=1161;
        static readonly int[][] CompositionColumnSilhouettes=
        {
            new[]{416,343,424,345,430,344,437,349,437,378,434,384,440,386,440,396,420,405,397,395,397,386,403,382,403,355,408,350},
            new[]{584,476,593,478,599,476,604,481,604,518,609,522,608,536,588,544,568,534,568,527,575,521,574,492,570,490,571,483},
            new[]{848,434,860,433,866,438,870,438,879,445,879,456,871,463,871,503,876,508,870,516,850,519,837,510,838,502,842,493,841,462,833,458,833,448,829,445,832,439},
            new[]{361,725,371,727,374,731,383,733,383,744,377,748,376,773,380,776,377,785,363,787,345,778,345,770,349,766,349,740,345,737,346,730,353,728},
            new[]{344,266,354,269,356,276,356,316,360,320,367,323,368,333,337,353,317,348,300,337,300,327,308,319,309,294,307,289,312,281,321,275,331,279,336,274,338,269},
            new[]{627,354,636,356,641,360,649,362,650,369,646,374,646,393,654,397,654,404,634,415,615,405,615,397,621,391,620,370,615,366,616,360},
            new[]{688,637,697,638,704,643,704,651,700,654,700,669,707,674,706,682,692,691,676,683,676,676,680,669,680,651,675,648,677,642}
        };
        static readonly Vector2[] CompositionColumnFeet=
        {
            new Vector2(420,401),new Vector2(588,540),new Vector2(856,515),
            new Vector2(363,782),new Vector2(334,346),new Vector2(634,410),new Vector2(692,686)
        };

        /// <summary>
        /// Intentional, exactly registered overdraw: the original columns remain in the three
        /// base layers. These seven overlays are meshes over the same shared texture (no pixel
        /// copy), cut to the traced silhouette and sorted by their footing, so they only correct
        /// foreground character occlusion. Fading one reveals the identical background, never a hole.
        /// Navigation already owns the column footings; this creates no collision geometry.
        /// </summary>
        static void BuildCompositionColumnOcclusion(Texture2D texture,float ppuX,float ppuY,UnderworldCompositionScene scene)
        {
            int tw=texture.width,th=texture.height;
            scene.ForegroundRenderers=new SpriteRenderer[CompositionColumnSilhouettes.Length];
            for(int column=0;column<CompositionColumnSilhouettes.Length;column++)
            {
                int[] polygon=CompositionColumnSilhouettes[column];
                // Reference coordinates start at the source image's top-left; texture pixels at the bottom-left.
                var points=new Vector2[polygon.Length/2];
                for(int i=0;i<polygon.Length;i+=2)
                    points[i/2]=new Vector2(Mathf.Clamp((float)polygon[i]/ColumnReferenceWidth*tw,0,tw),
                        Mathf.Clamp((1f-(float)polygon[i+1]/ColumnReferenceHeight)*th,0,th));
                var triangles=MapArtMesh.Triangulate(points);
                if(triangles.Length<3)continue;
                Vector2 sourceFoot=CompositionColumnFeet[column];
                var footPixel=new Vector2(sourceFoot.x/ColumnReferenceWidth*tw,(1f-sourceFoot.y/ColumnReferenceHeight)*th);
                var foot=new Vector2(footPixel.x/ppuX,footPixel.y/ppuY);
                var sprite=Sprite.Create(texture,new Rect(0,0,tw,th),new Vector2(footPixel.x/tw,footPixel.y/th),ppuX,0,SpriteMeshType.FullRect);
                sprite.OverrideGeometry(points,triangles);
                sprite.name="hollow_depths_column_foreground_"+(column+1);scene.Own(sprite);
                var go=new GameObject("Composition foreground column "+(column+1));
                go.transform.SetParent(scene.transform,false);go.transform.position=new Vector3(foot.x,foot.y,0);
                go.transform.localScale=MapArtMesh.Registration(ppuX,ppuY);
                var renderer=go.AddComponent<SpriteRenderer>();renderer.sprite=sprite;renderer.color=Color.white;
                renderer.sortingOrder=YSort.OrderFor(foot.y);
                scene.ForegroundRenderers[column]=renderer;
                TreeFade.Attach(go);
            }
        }
    }
}
