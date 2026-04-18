Shader "App/PlanetSegmentMask"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Color", Color) = (1,1,1,1)
        _TextureTiling ("Texture Tiling", Vector) = (1,1,0,0)
        _InnerRadiusNorm ("Inner Radius", Range(0, 0.5)) = 0
        _OuterRadiusNorm ("Outer Radius", Range(0, 0.5)) = 0.5
        _HalfAngleDeg ("Half Angle", Range(0, 180)) = 45
        _EnableOutline ("Enable Outline", Float) = 1
        _OutlineColor ("Outline Color", Color) = (0,0,0,1)
        _OutlineNorm ("Outline Width", Range(0, 0.2)) = 0.01
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "RenderType"="Transparent"
            "IgnoreProjector"="True"
            "CanUseSpriteAtlas"="True"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        Blend One OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex : POSITION;
                float2 texcoord : TEXCOORD0;
                fixed4 color : COLOR;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
                float2 localPos : TEXCOORD1;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _TextureTiling;
            fixed4 _Color;
            float _InnerRadiusNorm;
            float _OuterRadiusNorm;
            float _HalfAngleDeg;
            float _EnableOutline;
            fixed4 _OutlineColor;
            float _OutlineNorm;

            v2f vert(appdata_t IN)
            {
                v2f OUT;
                OUT.vertex = UnityObjectToClipPos(IN.vertex);
                OUT.uv = TRANSFORM_TEX(IN.texcoord, _MainTex);
                OUT.color = IN.color;
                OUT.localPos = IN.vertex.xy;
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                float radius = length(IN.localPos);
                if (radius < _InnerRadiusNorm || radius > _OuterRadiusNorm)
                    discard;

                float angleDeg = degrees(atan2(IN.localPos.y, IN.localPos.x));
                if (abs(angleDeg) > _HalfAngleDeg)
                    discard;

                float2 tiledUv = frac(IN.uv * _TextureTiling.xy + _TextureTiling.zw);
                fixed4 baseColor = tex2D(_MainTex, tiledUv) * IN.color * _Color;

                if (_EnableOutline > 0.5)
                {
                    float nearInner = abs(radius - _InnerRadiusNorm) <= _OutlineNorm;
                    float nearOuter = abs(_OuterRadiusNorm - radius) <= _OutlineNorm;
                    float nearAngle = (_HalfAngleDeg - abs(angleDeg)) <= (_OutlineNorm * 180.0);
                    if (nearInner || nearOuter || nearAngle)
                        return _OutlineColor;
                }

                return baseColor;
            }
            ENDCG
        }
    }
}
