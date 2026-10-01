Shader "DotRPG/SwimmingKoi"
{
    Properties
    {
        [PerRendererData] _MainTex ("Koi", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        [HideInInspector] _RendererColor ("RendererColor", Color) = (1,1,1,1)
        [HideInInspector] _Flip ("Flip", Vector) = (1,1,1,1)
        _WaterTime ("Time", Float) = 0
        [PerRendererData] _Phase ("Tail phase", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "CanUseSpriteAtlas"="False" }
        Cull Off ZWrite Off Lighting Off
        Blend One OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex SpriteVert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnitySprites.cginc"
            float _WaterTime, _Phase;
            fixed4 frag(v2f i) : SV_Target
            {
                float2 uv = i.texcoord;
                float t = floor(_WaterTime*12)/12;
                float tail = saturate((.72-uv.x)/.65);
                uv.y += sin(t*6.5-uv.x*8+_Phase)*tail*tail*.075;
                // Sample a fixed logical pixel grid, keeping the generated fins crisp as they bend.
                uv = (floor(uv*float2(80,40))+.5)/float2(80,40);
                fixed4 c = SampleSpriteTexture(uv) * i.color;
                c.a *= step(.28,c.a);
                c.rgb *= .96 + .04*sin(t*2 + uv.x*9);
                c.rgb *= c.a;
                return c;
            }
            ENDCG
        }
    }
}
