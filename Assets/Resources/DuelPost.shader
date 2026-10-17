Shader "Duel/Post"
{
    Properties { _MainTex ("Texture", 2D) = "white" {} }
    SubShader {
        Cull Off ZWrite Off ZTest Always
        Pass {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float _Pulse, _Damage, _Flash;
            float _Lines, _Ink, _Bars;
            float4 _Focus;
            fixed4 frag(v2f_img i):SV_Target {
                float2 uv=i.uv, d=(uv-.5);
                float shift=_Pulse*.002;
                float3 c=tex2D(_MainTex,uv).rgb;
                c.r=tex2D(_MainTex,uv+d*shift).r; c.b=tex2D(_MainTex,uv-d*shift).b;
                float3 bloom=0;
                for(int j=0;j<8;j++) {
                    float a=j*.785398;
                    float2 offset=float2(cos(a),sin(a))*_MainTex_TexelSize.xy*4;
                    bloom+=max(0,tex2D(_MainTex,uv+offset).rgb-.72);
                }
                c+=bloom*.08;
                float edge=smoothstep(.18,.75,length(d)); c*=1-edge*.55;
                c+=float3(.22,.015,.005)*edge*_Damage;
                c=lerp(c,float3(.8,.93,.92),_Flash*.42);
                float luminance=dot(c,float3(.299,.587,.114));
                c=lerp(c, smoothstep(.13,.7,luminance).xxx, _Ink);
                float2 radial=uv-_Focus.xy;
                float angle=atan2(radial.y,radial.x);
                float streak=pow(abs(sin(angle*71+sin(angle*13)*8)),28);
                float outer=smoothstep(.17,.65,length(radial));
                c+=streak*outer*_Lines*.55;
                float bars=step(uv.y,.065*_Bars)+step(1-.065*_Bars,uv.y);
                c=lerp(c,float3(.008,.01,.012),saturate(bars));
                return float4(c,1);
            }
            ENDCG
        }
    }
}
