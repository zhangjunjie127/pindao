Shader "Duel/Sprite"
{
    Properties {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Fog ("Fog", Float) = 0
        _FogColor ("Fog color", Color) = (0.1,0.2,0.2,1)
        _Flash ("Flash", Float) = 0
        _Recolor ("Recolor", Float) = 0
        _Accent ("Accent", Color) = (1,0.7,0.2,1)
    }
    SubShader {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "CanUseSpriteAtlas"="True" }
        Cull Off ZWrite Off Blend SrcAlpha OneMinusSrcAlpha
        Pass {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct app { float4 vertex:POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; };
            struct vary { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; };
            sampler2D _MainTex;
            float4 _Color, _FogColor, _Accent;
            float _Fog, _Flash, _Recolor;
            vary vert(app v) { vary o; o.pos=UnityObjectToClipPos(v.vertex); o.uv=v.uv; o.color=v.color*_Color; return o; }
            fixed4 frag(vary i):SV_Target {
                float4 s=tex2D(_MainTex,i.uv);
                float mask=saturate((s.r-s.b)*3)*_Recolor;
                s.rgb=lerp(s.rgb, _Accent.rgb*max(s.r,.2),mask*.8);
                s.rgb=lerp(s.rgb*i.color.rgb,_FogColor.rgb,_Fog);
                s.rgb=lerp(s.rgb, float3(1,1,.9), _Flash);
                return float4(s.rgb,s.a*i.color.a);
            }
            ENDCG
        }
    }
}
