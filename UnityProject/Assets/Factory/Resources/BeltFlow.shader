Shader "PuzzleFactory/BeltFlow"
{
    Properties { _Travel ("Distance traveled", Float) = 0 }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float _Travel;
            struct Input { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct Output { float4 position : SV_POSITION; float2 uv : TEXCOORD0; };
            Output vert(Input input)
            {
                Output output;
                output.position = UnityObjectToClipPos(input.vertex);
                output.uv = input.uv;
                return output;
            }
            fixed4 frag(Output input) : SV_Target
            {
                float slat = frac((input.uv.y - _Travel) / .22);
                float seam = smoothstep(.86, .98, slat);
                float highlight = smoothstep(.02, .14, slat) * (1 - smoothstep(.18, .28, slat));
                float side = smoothstep(.33, .5, abs(input.uv.x - .5));
                float3 rubber = float3(.082, .115, .169);
                return fixed4(rubber * (1 + highlight * .32 - seam * .52 + side * .12), 1);
            }
            ENDCG
        }
    }
}
