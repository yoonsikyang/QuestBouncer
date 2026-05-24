Shader "Custom/VertexColorHighlight"
{
    Properties
    {
        _SliceHighlightColor ("Highlight Color", Color) = (1, 0, 0, 1)
        _SliceHighlightThickness ("Highlight Thickness", Range(0.0001, 0.05)) = 0.005
        _SlicePlanePoint ("Plane Point", Vector) = (0,0,0,0)
        _SlicePlaneNormal ("Plane Normal", Vector) = (0,1,0,0)
        [Toggle] _EnableHighlight ("Enable Highlight", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        LOD 100
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off

        Pass
        {
            Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float3 worldPos : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            fixed4 _SliceHighlightColor;
            float _SliceHighlightThickness;
            float4 _SlicePlanePoint;
            float4 _SlicePlaneNormal;
            float _EnableHighlight;

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }
            
            fixed4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                
                if (_EnableHighlight < 0.5) discard;

                // Calculate distance to plane in world space
                float dist = dot(i.worldPos - _SlicePlanePoint.xyz, _SlicePlaneNormal.xyz);
                float absDist = abs(dist);
                float threshold = _SliceHighlightThickness;

                if (absDist > threshold) discard;
                
                // Smooth highlight band
                float t = 1.0 - smoothstep(0, threshold, absDist);
                fixed4 col = _SliceHighlightColor;
                col.a *= t;

                return col;
            }
            ENDCG
        }
    }
}
