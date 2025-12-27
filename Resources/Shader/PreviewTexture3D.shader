Shader "Hidden/PreviewTexture3D"
{
    Properties
    {
        _Volume("", 3D) =  ""{}
        _SliceDepths("", Vector) = (0.5, 0.5, 0.5, 0)
        _Steps("", Int) = 64
        _Density("", Float) = 1.0
        _Opacity("", Float) = 1.0
        _FilterColor("", Color) = (1,1,1,1)
        _Mode("", Float) = 0.0
        _Tiling("", Float) = 1.0
        
        _Cull("", Float) = 0.0
        _ZWrite("", Float) = 0.0
        _BlendSrc("", Float) = 1.0
        _BlendDst("", Float) = 0.0
    }
    
    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
        }
        
        Pass
        {
            Name "VolumePreview"
            Cull [_Cull]
            ZWrite [_ZWrite]
            ZTest LEqual
            Blend [_BlendSrc] [_BlendDst]
            
            CGPROGRAM

            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex: POSITION;
                float2 uv: TEXCOORD0;
                float3 normal: NORMAL;
            };

            struct v2f
            {
                float4 positionCS: SV_POSITION;
                float3 positionOS: TEXCOORD0;
                float3 positionWS: TEXCOORD1;
                float3 viewDirOS: TEXCOORD2;
                float2 uv: TEXCOORD3;
                float3 normalWS: TEXCOORD4;
                
            };

            sampler3D _Volume;
            int _Steps;
            float _Density;
            float _Opacity;
            float4 _FilterColor;
            float _Mode;
            float3 _SliceDepths;
            float _Tiling;

            v2f vert(appdata v)
            {
                v2f o;
                o.positionCS = UnityObjectToClipPos(v.vertex);
                o.positionOS = v.vertex.xyz + 0.5;
                o.positionWS = mul(unity_ObjectToWorld, v.vertex).xyz;

                float3 viewDirWS = o.positionWS - _WorldSpaceCameraPos;
                o.viewDirOS = normalize(mul((float3x3)unity_WorldToObject, viewDirWS));

                o.uv = v.uv;
                o.normalWS = UnityObjectToWorldNormal(v.normal);

                return o;
            }

            half4 RenderSlice(float2 uv, float3 normalWS)
            {
                float3 uvw;
                float3 absNormal = abs(normalWS);

                if (absNormal.x > 0.9)
                {
                    uvw = float3(_SliceDepths.x, uv.x * _Tiling, uv.y * _Tiling);
                }
                else if (absNormal.y > 0.9)
                {
                    uvw = float3(uv.x * _Tiling, _SliceDepths.y, uv.y * _Tiling);
                }
                else if (absNormal.z > 0.9)
                {
                    uvw = float3(uv.x * _Tiling, uv.y * _Tiling, _SliceDepths.z);
                }

                float4 color = tex3D(_Volume, uvw);
                color.rgb *= _FilterColor.rgb;
                return color;
            }

            half4 RenderVolume(float3 positionOS, float3 viewDirOS)
            {
                float3 rayStart = positionOS;
                float3 rayDir = normalize(viewDirOS);

                float3 invRayDir = rcp(rayDir);
                // rayStart + t0 * rayDir = 0.0
                // rayStart + t1 * rayDir = 1.0
                float3 t0 = (0.0 - rayStart) * invRayDir; // distance to planes at x=0, y=0, z=0
                float3 t1 = (1.0 - rayStart) * invRayDir; // distance to planes at x=1, y=1, z=1
                float3 tmin = min(t0, t1);
                float3 tmax = max(t0, t1);

                float tNear = max(max(tmin.x, tmin.y), tmin.z);
                tNear = max(0, tNear);
                float tFar = min(min(tmax.x, tmax.y), tmax.z);

                if (tNear >= tFar || tFar <= 0) discard;

                float rayLength = tFar - tNear;

                float stepSize = rayLength / float(_Steps);
                float3 rayPos = rayStart + rayDir * tNear;

                float4 accColor = 0;
                
                [loop]
                for (int i = 0; i < _Steps; i++)
                {
                    if (accColor.a >= 0.99) break;

                    float4 sampleColor = tex3D(_Volume, rayPos * _Tiling);
                    sampleColor.rgb *= _FilterColor.rgb;

                    float luminance = dot(sampleColor.rgb, float3(0.299, 0.587, 0.114));
                    sampleColor.a = luminance * _Opacity;
                    
                    sampleColor.a = 1.0 - exp(-sampleColor.a * stepSize * _Density * 5.0);

                    accColor.rgb += (1.0 - accColor.a) * sampleColor.a * sampleColor.rgb;
                    accColor.a += (1.0 - accColor.a) * sampleColor.a;

                    rayPos += rayDir * stepSize;

                    if (any(rayPos < 0.0) || any(rayPos > 1.0)) break;
                }

                return accColor;
            }

            half4 frag(v2f i) : SV_Target
            {
                half4 color;
                if (_Mode == 0.0)
                {
                    color = RenderVolume(i.positionOS, i.viewDirOS);
                }
                else
                {
                    color = RenderSlice(i.uv, i.normalWS);
                }

                return color;
            }
            
            ENDCG
        }

//        Pass
//        {
//            Name "SlicePreview"
//            Cull Off
//            ZWrite On
//            ZTest LEqual
//            Blend Off
//            
//            CGPROGRAM
//
//            #pragma vertex vert
//            #pragma fragment frag
//            #include "UnityCG.cginc"
//
//            struct appdata
//            {
//                float4 vertex: POSITION;
//                float2 uv: TEXCOORD0;
//                float3 normal: NORMAL;
//            };
//
//            struct v2f
//            {
//                float4 positionCS: SV_POSITION;
//                float2 uv : TEXCOORD0;
//                float3 normalWS : TEXCOORD1;
//            };
//
//            sampler3D _Volume;
//            float3 _SliceDepths;
//            float4 _FilterColor;
//
//            v2f vert(appdata v)
//            {
//                v2f o;
//                o.positionCS = UnityObjectToClipPos(v.vertex);
//                o.uv = v.uv;
//                o.normalWS = UnityObjectToWorldNormal(v.normal);
//                return o;
//            }
//
//            half4 frag(v2f i) : SV_Target
//            {
//                float3 uvw;
//                float3 absNormal = abs(i.normalWS);
//
//                if (absNormal.x > 0.9)
//                {
//                    uvw = float3(_SliceDepths.x, i.uv.x, i.uv.y);
//                }
//                else if (absNormal.y > 0.9)
//                {
//                    uvw = float3(i.uv.x, _SliceDepths.y, i.uv.y);
//                }
//                else if (absNormal.z > 0.9)
//                {
//                    uvw = float3(i.uv.x, i.uv.y, _SliceDepths.z);
//                }
//
//                float4 color = tex3D(_Volume, uvw);
//                color.rgb *= _FilterColor.rgb;
//                return color;
//            }
//            
//            ENDCG
//        }
    }
}
