Shader "LightweightFpsCounter/Overlay"
{
    Properties
    {
        _MainTex ("Font Atlas", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "Queue" = "Overlay" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Cull Off
        Lighting Off
        ZWrite Off
        ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            // xy: normalized anchor (0 = left/bottom, 1 = right/top), zw: pixel offset.
            float4 _AnchorParams;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            v2f vert(appdata v)
            {
                v2f o;
                // Vertex positions are authored in pixels; map them straight to clip space.
                float2 pixel = v.vertex.xy + _AnchorParams.xy * _ScreenParams.xy + _AnchorParams.zw;
                float2 ndc = pixel / _ScreenParams.xy * 2.0 - 1.0;
                o.pos = float4(ndc.x, ndc.y * _ProjectionParams.x, UNITY_NEAR_CLIP_VALUE, 1.0);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // Negative UVs mark the solid background panel.
                if (i.uv.x < 0.0) return i.color;
                fixed4 col = i.color;
                col.a *= tex2D(_MainTex, i.uv).a;
                return col;
            }
            ENDCG
        }
    }
}
