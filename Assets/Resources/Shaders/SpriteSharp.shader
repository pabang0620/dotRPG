// Crisp pixel-art sprite shader for the high-resolution (32px per tile) town and forest art.
// The camera's integer zoom is chosen for the 16px art, so 32px art ends up at a non-integer
// scale (e.g. 3.5 screen pixels per texel at 1440p). Plain point sampling then draws texels
// 3 and 4 pixels wide in turn and shimmers while the camera moves. This shader samples the
// (bilinear-filtered) texture so every texel stays a sharp square and only the one screen pixel
// on a texel boundary is blended ("sharp bilinear"). At integer scales it matches point sampling.
// Lives in Resources so it is always part of the build (loaded by FxMaterials.Sharp).
Shader "DotRPG/SpriteSharp"
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
        Blend One OneMinusSrcAlpha

        Pass
        {
        CGPROGRAM
            #pragma vertex SpriteVert
            #pragma fragment SharpFrag
            #pragma target 3.0
            #pragma multi_compile_instancing
            #pragma multi_compile_local _ PIXELSNAP_ON
            #pragma multi_compile _ ETC1_EXTERNAL_ALPHA
            #include "UnitySprites.cginc"

            float4 _MainTex_TexelSize;

            fixed4 SharpFrag(v2f IN) : SV_Target
            {
                float2 size = _MainTex_TexelSize.zw;
                float2 t = IN.texcoord * size;
                // Nearest texel boundary; blend across it over exactly one screen pixel, elsewhere
                // sample the texel centre (a pure colour).
                float2 seam = floor(t + 0.5);
                float2 perPixel = max(fwidth(t), 1e-5);
                t = seam + clamp((t - seam) / perPixel, -0.5, 0.5);
                fixed4 c = SampleSpriteTexture(t / size) * IN.color;
                c.rgb *= c.a;
                return c;
            }
        ENDCG
        }
    }
}
