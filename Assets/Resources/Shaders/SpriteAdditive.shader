// Additive sprite shader for skill effects: the sprite's colour is ADDED to what is behind it,
// so glows, flashes and lightning read as light. Lives in Resources so it is always part of the
// build (loaded by FxMaterials). Uses Unity's own sprite vertex code, so SpriteRenderer colour,
// flipping and LineRenderer vertex colours all work exactly as with Sprites/Default.
Shader "DotRPG/SpriteAdditive"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        [HideInInspector] _RendererColor ("RendererColor", Color) = (1,1,1,1)
        [HideInInspector] _Flip ("Flip", Vector) = (1,1,1,1)
        [PerRendererData] _AlphaTex ("External Alpha", 2D) = "white" {}
        [PerRendererData] _EnableExternalAlpha ("Enable External Alpha", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        Blend SrcAlpha One

        Pass
        {
        CGPROGRAM
            #pragma vertex SpriteVert
            #pragma fragment AdditiveFrag
            #pragma target 2.0
            #pragma multi_compile_instancing
            #pragma multi_compile_local _ PIXELSNAP_ON
            #pragma multi_compile _ ETC1_EXTERNAL_ALPHA
            #include "UnitySprites.cginc"

            fixed4 AdditiveFrag(v2f IN) : SV_Target
            {
                // Not premultiplied: the blend state scales the colour by alpha.
                return SampleSpriteTexture(IN.texcoord) * IN.color;
            }
        ENDCG
        }
    }
}
