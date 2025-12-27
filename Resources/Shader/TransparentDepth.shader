Shader "Hidden/Juan/TransparentDepth"
{
    SubShader
    {
        Pass
        {
            ZWrite On
            ZTest LEqual
            Cull Back
            Blend Off
            
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            
            TEXTURE2D(_CameraTexture);
            SAMPLER(sampler_CameraTexture);

            struct appdata
            {
                float4 positionOS : POSITION;
            };

            struct v2f
            {
                float4 positionCS : SV_POSITION;
                float4 screenPos: TEXCOORD0;
                float depthVS : TEXCOORD1;
            };
            
            float remap(float v, float minOld, float maxOld, float minNew, float maxNew)
            {
                return minNew + (v - minOld) * (maxNew - minNew) / max(maxOld - minOld, 1e-5);
            }

            v2f vert (appdata v)
            {
                v2f o;
                o.positionCS = TransformObjectToHClip(v.positionOS);
                o.screenPos = ComputeScreenPos(o.positionCS);
                
                float3 positionVS = TransformWorldToView(TransformObjectToWorld(v.positionOS));
                o.depthVS = -positionVS.z;
                
                #if UNITY_REVERSED_Z
				    o.positionCS.z = min(o.positionCS.z, UNITY_NEAR_CLIP_VALUE);
				#else
				    o.positionCS.z = max(o.positionCS.z, UNITY_NEAR_CLIP_VALUE);
				#endif
                
                return o;
            }

            half2 frag (v2f i) : SV_Target
            {
                float2 screenUV = i.screenPos.xy / i.screenPos.w;
                
                float depth = SampleSceneDepth(screenUV);
                float linearDepth = LinearEyeDepth(depth, _ZBufferParams);
                
                if (i.depthVS > linearDepth) discard;
                return half2(i.positionCS.z, 0);
            }
            ENDHLSL
        }
    }
}
