Shader "Hanyang/Legacy Bounds Shell"
{
    Properties
    {
        _Alpha ("Alpha", Range(0, 1)) = 0.9
        _Intensity ("Intensity", Range(0, 1)) = 0.62
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float _Alpha;
            float _Intensity;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 centered = i.uv - float2(0.5, 0.5);
                centered *= float2(0.9, 1.05);

                float radial = saturate(1.0 - dot(centered, centered) * 2.25);
                radial = smoothstep(0.05, 0.95, radial);

                float verticalMask = smoothstep(0.04, 0.22, i.uv.y) * smoothstep(0.96, 0.78, i.uv.y);
                float horizontalLight = smoothstep(0.18, 0.92, i.uv.x);
                float intensity = (0.08 + (_Intensity * 0.7 * horizontalLight)) * radial;
                float alpha = radial * verticalMask * _Alpha;

                return fixed4(intensity, intensity, intensity, alpha);
            }
            ENDCG
        }
    }
}
