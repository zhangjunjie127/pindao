Shader "Duel/Water"
{
    Properties { _MainTex ("Texture", 2D) = "white" {} }
    SubShader {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Cull Off ZWrite Off Blend SrcAlpha OneMinusSrcAlpha
        Pass {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct app { float4 vertex:POSITION; float2 uv:TEXCOORD0; };
            struct vary { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; };
            vary vert(app v) { vary o; o.pos=UnityObjectToClipPos(v.vertex); o.uv=v.uv; return o; }
            fixed4 frag(vary i):SV_Target {
                float t=_Time.y;
                float ripple=sin(i.uv.y*143+sin(i.uv.x*30+t)*2+t*1.5);
                float glitter=pow(saturate(ripple),18)*(.25+.75*pow(saturate(sin(i.uv.x*52-i.uv.y*19+t*.6)),6));
                float3 col=lerp(float3(.025,.075,.083),float3(.13,.25,.22),i.uv.y);
                col+=float3(.24,.5,.43)*glitter*.3;
                return float4(col,.95);
            }
            ENDCG
        }
    }
}
