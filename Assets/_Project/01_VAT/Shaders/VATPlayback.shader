Shader "VAT/Playback"
{
    Properties
    {
        _BaseMap ("Base Map", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Normal Scale", Float) = 1.0
        _MetallicGlossMap ("Metallic (R) Smoothness (A)", 2D) = "white" {}
        _Metallic ("Metallic", Range(0, 1)) = 0.0
        _Smoothness ("Smoothness", Range(0, 1)) = 0.5

        [Header(VAT)]
        _PositionTex ("Position Texture", 2D) = "black" {}
        _NormalTex ("Normal Texture", 2D) = "bump" {}
        _FrameCount ("Frame Count", Float) = 1
        _Duration ("Duration", Float) = 1
        _VertexCount ("Vertex Count", Float) = 1
        _PosMin ("Position Min", Vector) = (0, 0, 0, 0)
        _PosMax ("Position Max", Vector) = (1, 1, 1, 0)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            // Instance Data Texture (RenderTexture as data container)
            // Layout: Width = InstanceCount, Height = 2
            // Row 0: Position (RGB) + TimeOffset (A)
            // Row 1: cosY (R) + sinY (G) + Scale (B) + Reserved (A)
            // All trigonometry pre-computed on CPU.
            TEXTURE2D(_InstanceDataTex);
            SAMPLER(sampler_InstanceDataTex);
            float4 _InstanceDataTex_TexelSize;

            struct Attributes
            {
                uint vertexID : SV_VertexID;
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 tangentWS : TEXCOORD2;
                float2 uv : TEXCOORD3;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BumpMap);
            SAMPLER(sampler_BumpMap);
            TEXTURE2D(_MetallicGlossMap);
            SAMPLER(sampler_MetallicGlossMap);
            TEXTURE2D(_PositionTex);
            SAMPLER(sampler_PositionTex);
            TEXTURE2D(_NormalTex);
            SAMPLER(sampler_NormalTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float _BumpScale;
                float _Metallic;
                float _Smoothness;
                float _FrameCount;
                float _Duration;
                float _VertexCount;
                float4 _PosMin;
                float4 _PosMax;
                float4 _PositionTex_TexelSize;
                float4 _NormalTex_TexelSize;
            CBUFFER_END

            // Get instance data from RenderTexture (all values pre-computed on CPU)
            void GetInstanceData(uint instanceID, out float4x4 objectToWorld, out float timeOffset)
            {
                // UV calculation (sample texel center)
                float u = (instanceID + 0.5) * _InstanceDataTex_TexelSize.x;

                // Row 0: Position (RGB) + TimeOffset (A) - sample at v=0.25 (center of row 0)
                float4 row0 = SAMPLE_TEXTURE2D_LOD(_InstanceDataTex, sampler_InstanceDataTex, float2(u, 0.25), 0);
                float3 position = row0.xyz;
                timeOffset = row0.w;

                // Row 1: cosY (R) + sinY (G) + Scale (B) - sample at v=0.75 (center of row 1)
                float4 row1 = SAMPLE_TEXTURE2D_LOD(_InstanceDataTex, sampler_InstanceDataTex, float2(u, 0.75), 0);
                float cosY = row1.x;  // Pre-computed on CPU
                float sinY = row1.y;  // Pre-computed on CPU
                float scale = row1.z;

                // Unity TRS matrix format (column-major, translation in column 3)
                // Y-axis rotation: row0=(cosY, 0, sinY), row2=(-sinY, 0, cosY)
                objectToWorld = float4x4(
                    cosY * scale,   0,     sinY * scale,   position.x,
                    0,              scale, 0,              position.y,
                    -sinY * scale,  0,     cosY * scale,   position.z,
                    0,              0,     0,              1
                );
            }

            float3 SamplePosition(uint vertexID, float frame)
            {
                float textureWidth = _PositionTex_TexelSize.z;
                float u = (vertexID + 0.5) / textureWidth;
                float v = (frame + 0.5) / _FrameCount;

                float4 encoded = SAMPLE_TEXTURE2D_LOD(_PositionTex, sampler_PositionTex, float2(u, v), 0);

                // Denormalize
                float3 position = lerp(_PosMin.xyz, _PosMax.xyz, encoded.xyz);
                return position;
            }

            float3 SampleNormal(uint vertexID, float frame)
            {
                float textureWidth = _NormalTex_TexelSize.z;
                float u = (vertexID + 0.5) / textureWidth;
                float v = (frame + 0.5) / _FrameCount;

                float4 encoded = SAMPLE_TEXTURE2D_LOD(_NormalTex, sampler_NormalTex, float2(u, v), 0);

                // Decode normal from [0,1] to [-1,1]
                float3 normal = encoded.xyz * 2.0 - 1.0;
                return normalize(normal);
            }

            Varyings vert(Attributes input, uint instanceID : SV_InstanceID)
            {
                Varyings output;

                // Get instance data from RenderTexture
                float4x4 objectToWorld;
                float timeOffset;
                GetInstanceData(instanceID, objectToWorld, timeOffset);

                // Calculate current frame with interpolation (per-instance time offset)
                float time = fmod(_Time.y + timeOffset, _Duration);
                float rawFrame = time / _Duration * _FrameCount;
                float frame0 = floor(rawFrame);
                float frame1 = fmod(frame0 + 1, _FrameCount); // Wrap for seamless loop
                float t = frac(rawFrame);

                // Sample and interpolate position from VAT
                float3 pos0 = SamplePosition(input.vertexID, frame0);
                float3 pos1 = SamplePosition(input.vertexID, frame1);
                float3 vatPosition = lerp(pos0, pos1, t);

                // Sample and interpolate normal from VAT
                float3 norm0 = SampleNormal(input.vertexID, frame0);
                float3 norm1 = SampleNormal(input.vertexID, frame1);
                float3 vatNormal = normalize(lerp(norm0, norm1, t));

                // Transform position to world space using instance matrix
                float4 positionWS = mul(objectToWorld, float4(vatPosition, 1.0));
                output.positionWS = positionWS.xyz;

                // Transform to clip space
                output.positionCS = mul(UNITY_MATRIX_VP, positionWS);

                // Transform normal to world space (assumes uniform scale)
                float3x3 objectToWorld3x3 = (float3x3)objectToWorld;
                output.normalWS = normalize(mul(objectToWorld3x3, vatNormal));

                // Transform tangent to world space
                float3 tangentWS = normalize(mul(objectToWorld3x3, input.tangentOS.xyz));
                output.tangentWS = float4(tangentWS, input.tangentOS.w);

                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);

                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // Sample base texture
                half4 baseMap = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                half3 albedo = baseMap.rgb * _BaseColor.rgb;

                // Sample metallic/smoothness
                half4 metallicGloss = SAMPLE_TEXTURE2D(_MetallicGlossMap, sampler_MetallicGlossMap, input.uv);
                half metallic = metallicGloss.r * _Metallic;
                half smoothness = metallicGloss.a * _Smoothness;
                half roughness = 1.0 - smoothness;

                // Normal mapping (tangent space normal map on top of VAT normal)
                float3 normalWS = normalize(input.normalWS);
                float3 tangentWS = normalize(input.tangentWS.xyz);
                float3 bitangentWS = cross(normalWS, tangentWS) * input.tangentWS.w;
                float3x3 tangentToWorld = float3x3(tangentWS, bitangentWS, normalWS);

                float3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv), _BumpScale);
                normalWS = normalize(mul(normalTS, tangentToWorld));

                // PBR lighting setup
                float3 viewDirWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                Light mainLight = GetMainLight();

                // Diffuse (Lambert)
                float NdotL = saturate(dot(normalWS, mainLight.direction));

                // Specular (simplified GGX)
                float3 halfDir = normalize(mainLight.direction + viewDirWS);
                float NdotH = saturate(dot(normalWS, halfDir));
                float NdotV = saturate(dot(normalWS, viewDirWS));

                // Roughness-based specular
                float roughness2 = roughness * roughness;
                float d = NdotH * NdotH * (roughness2 - 1.0) + 1.0;
                float specularTerm = roughness2 / (d * d * max(0.1, NdotL * NdotV) * (roughness * 4.0 + 2.0));

                // Fresnel (Schlick approximation)
                float3 F0 = lerp(float3(0.04, 0.04, 0.04), albedo, metallic);
                float3 fresnel = F0 + (1.0 - F0) * pow(1.0 - saturate(dot(halfDir, viewDirWS)), 5.0);

                // Combine diffuse and specular
                float3 diffuseColor = albedo * (1.0 - metallic);
                float3 diffuse = diffuseColor * mainLight.color * NdotL;
                float3 specular = specularTerm * fresnel * mainLight.color * NdotL;

                // Ambient (simple hemisphere)
                float3 ambient = albedo * 0.1;

                float3 finalColor = diffuse + specular + ambient;

                return half4(finalColor, 1.0);
            }
            ENDHLSL
        }
    }

    // Disable shadow casting - VAT instances don't use FallBack ShadowCaster
    // (FallBack would cast T-pose shadows + cause CPU overhead for 1000+ instances)
    FallBack Off
}
