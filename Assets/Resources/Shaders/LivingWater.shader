Shader "DotRPG/LivingWater"
{
    Properties
    {
        [PerRendererData] _MainTex ("Ground", 2D) = "white" {}
        [PerRendererData] _WaterMask ("Painted water mask", 2D) = "black" {}
        _Color ("Tint", Color) = (1,1,1,1)
        [HideInInspector] _RendererColor ("RendererColor", Color) = (1,1,1,1)
        [HideInInspector] _Flip ("Flip", Vector) = (1,1,1,1)
        _Flow ("Flow tiles per second", Vector) = (.09,-.21,0,0)
        _WaterTime ("Time", Float) = 0
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
            sampler2D _WaterMask;
            float4 _MainTex_TexelSize, _ChunkOrigin, _Flow;
            float _WaterTime;
            float hash(float2 p) { return frac(sin(dot(p, float2(127.1,311.7))) * 43758.5453); }
            fixed4 frag(v2f i) : SV_Target
            {
                float2 size = _MainTex_TexelSize.zw;
                float2 texel = i.texcoord * size;
                float2 seam = floor(texel + .5);
                float2 sharp = seam + clamp((texel-seam)/max(fwidth(texel),1e-5),-.5,.5);
                fixed4 c = SampleSpriteTexture(sharp / size) * i.color;
                float4 mask = tex2D(_WaterMask, (floor(texel)+.5)/size);
                if (mask.r > .5)
                {
                    // Absolute map pixels keep flow continuous across all 256px ground chunks.
                    float t = floor(_WaterTime * 12) / 12;
                    float2 p = floor(texel) + _ChunkOrigin.xy;
                    float2 q = p - _Flow.xy * t * 32;
                    float wave = sin(q.y*.30 + sin(q.x*.115)*1.7) + .45*sin(q.y*.13-q.x*.075);
                    float shade = floor(wave * 2) * .017;
                    c.rgb *= 1 + shade;
                    // Short drifting highlights, with staggered gaps instead of full-width stripes.
                    float2 drift = q + float2(sin(q.y*.06)*3, sin(q.x*.09)*1.5);
                    float2 cell = floor(drift / float2(19,13));
                    float2 local = frac(drift / float2(19,13));
                    float ripple = step(.56,hash(cell)) * step(.20,local.x) * step(local.x,.68)
                        * step(abs(local.y - .5), .048);
                    float dist = mask.g * 255 / 4;
                    float rim = step(dist,5) * step(1.1,dist) * step(.25,sin(t*2.8+p.x*.21+p.y*.16));
                    c.rgb = lerp(c.rgb, float3(.70,.91,.96), ripple*.37*step(5,dist) + rim*.24);
                    if (mask.b > .5)
                    {
                        float fall = sin(p.y*.5 + t*19 + sin(p.x*.19)*2);
                        float streak = step(.35,fall) * step(.05,sin(p.x*.43));
                        c.rgb = lerp(c.rgb, float3(.80,.95,1), streak*.35);
                    }
                }
                c.rgb *= c.a;
                return c;
            }
            ENDCG
        }
    }
}
