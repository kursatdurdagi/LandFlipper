// Land Flipper — stilize çimen / çiçek shader'ı (URP).
// - Kökten uca renk geçişi (vertex color alpha = yükseklik oranı 0..1), vertex color RGB = ton çeşitliliği.
// - Rüzgâr salınımı GPU'da (uca doğru artar), kök sabit kalır.
// - Yumuşak (wrap) ışık, arazi ile uyumlu olsun diye normal yukarı doğru bükülür; uçta hafif ışık geçirme.
// - Gölge alır, gölge atmaz (performans). SRP Batcher + GPU instancing uyumlu.
Shader "LandFlipper/StylizedFoliage"
{
    Properties
    {
        _BaseColor ("Kök Rengi", Color) = (0.24, 0.45, 0.20, 1)
        _TipColor ("Uç Rengi", Color) = (0.70, 0.86, 0.38, 1)
        _GradientPower ("Geçiş Eğrisi", Range(0.3, 3)) = 1.1
        _UseVertexColor ("Vertex Rengi Etkisi", Range(0, 1)) = 1
        _WrapLight ("Yumuşak Işık", Range(0, 1)) = 0.5
        _NormalUp ("Normal Yukarı", Range(0, 1)) = 0.65
        _Translucency ("Işık Geçirme", Range(0, 1)) = 0.35
        _AmbientBoost ("Ortam Işığı", Range(0, 2)) = 1.0
        _WindStrength ("Rüzgâr Gücü", Range(0, 0.5)) = 0.09
        _WindSpeed ("Rüzgâr Hızı", Range(0, 5)) = 1.4
        _WindScale ("Rüzgâr Ölçeği", Range(0.01, 1)) = 0.18
        _WindDirection ("Rüzgâr Yönü (xz)", Vector) = (1, 0, 0.4, 0)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half4 _TipColor;
            half _GradientPower;
            half _UseVertexColor;
            half _WrapLight;
            half _NormalUp;
            half _Translucency;
            half _AmbientBoost;
            float _WindStrength;
            float _WindSpeed;
            float _WindScale;
            float4 _WindDirection;
        CBUFFER_END

        float3 ApplyWind(float3 positionWS, float height)
        {
            float2 dir = normalize(_WindDirection.xz + 1e-4);
            float t = _Time.y * _WindSpeed;
            float phase = dot(positionWS.xz, dir) * _WindScale;
            // büyük dalga + küçük titreşim
            float wave = sin(t + phase * 6.2831) * 0.65 + sin(t * 2.3 + positionWS.x * 1.7 + positionWS.z * 1.3) * 0.35;
            float gust = sin(t * 0.37 + phase * 2.0) * 0.5 + 0.5;
            float bend = height * height * _WindStrength * (0.55 + gust * 0.9);
            positionWS.xz += dir * wave * bend;
            positionWS.y -= abs(wave) * bend * 0.25;
            return positionWS;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half4 color : TEXCOORD2;
                half fogFactor : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                float3 ws = TransformObjectToWorld(v.positionOS.xyz);
                ws = ApplyWind(ws, v.color.a);
                o.positionWS = ws;
                o.positionCS = TransformWorldToHClip(ws);
                half3 n = TransformObjectToWorldNormal(v.normalOS);
                o.normalWS = normalize(lerp(n, half3(0, 1, 0), _NormalUp));
                o.color = v.color;
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i, bool isFront : SV_IsFrontFace) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                half h = saturate(i.color.a);
                half3 albedo = lerp(_BaseColor.rgb, _TipColor.rgb, pow(h, _GradientPower));
                // vertex renkleri sRGB yazıldı; linear projede çevrilmezse soluk görünür
                half3 vc = i.color.rgb;
                #if !defined(UNITY_COLORSPACE_GAMMA)
                    vc = SRGBToLinear(vc);
                #endif
                albedo *= lerp(half3(1, 1, 1), vc, _UseVertexColor);

                half3 n = normalize(i.normalWS);
                n = isFront ? n : normalize(lerp(-n, half3(0, 1, 0), _NormalUp));

                float4 shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                Light light = GetMainLight(shadowCoord);
                half shadow = light.shadowAttenuation * light.distanceAttenuation;

                half ndl = dot(n, light.direction);
                half diffuse = saturate((ndl + _WrapLight) / (1.0 + _WrapLight));
                // arkadan gelen ışıkta uçlar hafif parlasın
                half3 viewDir = normalize(GetWorldSpaceViewDir(i.positionWS));
                half back = pow(saturate(dot(viewDir, -light.direction)), 4.0) * _Translucency * h;

                half3 ambient = SampleSH(n) * _AmbientBoost;
                #if defined(_SCREEN_SPACE_OCCLUSION)
                    AmbientOcclusionFactor ao = GetScreenSpaceAmbientOcclusion(GetNormalizedScreenSpaceUV(i.positionCS));
                    ambient *= ao.indirectAmbientOcclusion;
                #endif
                // köke doğru hafif kendi gölgesi (yoğun çimde derinlik)
                half selfShade = lerp(0.62, 1.0, h);

                half3 color = albedo * (ambient * selfShade + light.color * (diffuse * shadow * selfShade + back * shadow));
                color = MixFog(color, i.fogFactor);
                return half4(color, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            struct Attributes { float4 positionOS : POSITION; half4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 ws = ApplyWind(TransformObjectToWorld(v.positionOS.xyz), v.color.a);
                o.positionCS = TransformWorldToHClip(ws);
                return o;
            }
            half frag(Varyings i) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; half4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; half3 normalWS : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 ws = ApplyWind(TransformObjectToWorld(v.positionOS.xyz), v.color.a);
                o.positionCS = TransformWorldToHClip(ws);
                o.normalWS = normalize(lerp(TransformObjectToWorldNormal(v.normalOS), half3(0, 1, 0), _NormalUp));
                return o;
            }
            half4 frag(Varyings i) : SV_Target { return half4(normalize(i.normalWS), 0); }
            ENDHLSL
        }
    }
    FallBack Off
}
