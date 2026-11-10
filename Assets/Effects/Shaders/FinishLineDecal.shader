// RoverOps - Finish Line Decal (simple hollow circle)
// Put this material on a Cube (remove its collider).
// Cube X/Z scale = circle size, Y scale = how far up/down it paints.
// Needs "Depth Texture" ON in the URP asset used by the platform (WebGL uses the Mobile asset).
// Works on WebGL / OpenGL ES (non-reversed depth) and with any Render Scale.
Shader "RoverOps/Finish Line Decal"
{
    Properties
    {
        _Color ("Color", Color) = (1, 1, 1, 1)
        _Radius ("Radius", Range(0, 1)) = 0.9
        _Thickness ("Thickness", Range(0, 1)) = 0.1
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent-100" }

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Front

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0                                  // fwidth / screen derivatives (WebGL 2 is fine)
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _Radius;
                float _Thickness;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings   { float4 positionCS : SV_POSITION; };

            Varyings vert(Attributes IN)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                return o;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // Find the surface point behind this pixel.
                // GetNormalizedScreenSpaceUV: correct with Render Scale (Mobile = 0.8) and render textures on every platform
                float2 uv = GetNormalizedScreenSpaceUV(IN.positionCS);
                #if UNITY_REVERSED_Z
                    float depth = SampleSceneDepth(uv);
                #else
                    float depth = lerp(UNITY_NEAR_CLIP_VALUE, 1, SampleSceneDepth(uv));
                #endif
                float3 worldPos = ComputeWorldSpacePosition(uv, depth, UNITY_MATRIX_I_VP);

                // Only paint inside the cube
                float3 objPos = TransformWorldToObject(worldPos);
                clip(0.5 - abs(objPos));

                // Hollow circle
                float r = length(objPos.xz) * 2.0;
                float aa = fwidth(r);
                float outer = 1.0 - smoothstep(_Radius - aa, _Radius, r);
                float inner = smoothstep(_Radius - _Thickness - aa, _Radius - _Thickness, r);

                return half4(_Color.rgb, _Color.a * outer * inner);
            }
            ENDHLSL
        }
    }
}
