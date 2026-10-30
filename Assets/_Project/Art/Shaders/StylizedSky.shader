// Land Flipper - stilize gökyüzü
// Renk geçişli gök + yumuşak parlamalı güneş diski + yavaşça kayan, kenarları net "cartoon" bulutlar.
// Güneş yönü sahnedeki ana ışıktan (RenderSettings.sun) gelir.
Shader "LandFlipper/StylizedSky"
{
    Properties
    {
        [Header(Sky)]
        _ZenithColor ("Zenith", Color) = (0.27, 0.50, 0.86, 1)
        _SkyColor ("Sky", Color) = (0.50, 0.72, 0.96, 1)
        _HorizonColor ("Horizon (sis rengiyle uyumlu)", Color) = (0.82, 0.89, 0.92, 1)
        _GroundColor ("Below Horizon", Color) = (0.62, 0.70, 0.62, 1)
        _HorizonHeight ("Horizon Height", Range(0.02, 1)) = 0.22
        _ZenithHeight ("Zenith Height", Range(0.1, 1.5)) = 0.75

        [Header(Sun)]
        [HDR] _SunColor ("Sun Disc", Color) = (4.2, 3.9, 3.3, 1)
        _SunSize ("Sun Size (radyan)", Range(0.005, 0.2)) = 0.045
        _SunEdge ("Sun Edge Softness", Range(0.0005, 0.05)) = 0.006
        _SunGlowColor ("Sun Glow", Color) = (1, 0.84, 0.6, 1)
        _SunGlow ("Glow Strength", Range(0, 2)) = 0.6
        _SunGlowFalloff ("Glow Falloff", Range(2, 128)) = 18
        _SunHaze ("Horizon Haze Toward Sun", Range(0, 1)) = 0.35

        [Header(Clouds)]
        _CloudColor ("Cloud Lit", Color) = (1, 1, 1, 1)
        _CloudShadowColor ("Cloud Shadow", Color) = (0.72, 0.79, 0.90, 1)
        _CloudRimColor ("Cloud Rim (güneş tarafı)", Color) = (1, 0.88, 0.68, 1)
        _CloudCoverage ("Coverage (yüksek = az bulut)", Range(0, 1)) = 0.52
        _CloudSoftness ("Edge Softness", Range(0.002, 0.3)) = 0.035
        _CloudScale ("Scale", Range(0.1, 6)) = 1.5
        _CloudSpeed ("Drift Speed", Range(0, 0.05)) = 0.0045
        _CloudOpacity ("Opacity", Range(0, 1)) = 0.95
        _CloudHeightFade ("Horizon Fade", Range(0.01, 0.5)) = 0.12
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" }
        Cull Off
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            half4 _ZenithColor, _SkyColor, _HorizonColor, _GroundColor;
            half _HorizonHeight, _ZenithHeight;
            half4 _SunColor, _SunGlowColor;
            half _SunSize, _SunEdge, _SunGlow, _SunGlowFalloff, _SunHaze;
            half4 _CloudColor, _CloudShadowColor, _CloudRimColor;
            half _CloudCoverage, _CloudSoftness, _CloudScale, _CloudSpeed, _CloudOpacity, _CloudHeightFade;

            struct appdata { float4 vertex : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; float3 dir : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.dir = v.vertex.xyz;
                return o;
            }

            float hash21 (float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float valueNoise (float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = hash21(i);
                float b = hash21(i + float2(1, 0));
                float c = hash21(i + float2(0, 1));
                float d = hash21(i + float2(1, 1));
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            float fbm (float2 p)
            {
                float v = 0.0;
                float a = 0.5;
                [unroll] for (int k = 0; k < 5; k++)
                {
                    v += a * valueNoise(p);
                    p = p * 2.03 + float2(17.1, 9.2);
                    a *= 0.5;
                }
                return v;
            }

            half4 frag (v2f i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float y = d.y;

                // ---- gök renk geçişi
                float up = saturate(y);
                half3 sky = lerp(_HorizonColor.rgb, _SkyColor.rgb, saturate(up / _HorizonHeight));
                sky = lerp(sky, _ZenithColor.rgb, saturate((up - _HorizonHeight) / max(0.01, _ZenithHeight - _HorizonHeight)));
                half3 col = y >= 0 ? sky : lerp(_HorizonColor.rgb, _GroundColor.rgb, saturate(-y * 5.0));

                // ---- güneş
                float3 L = normalize(_WorldSpaceLightPos0.xyz);
                float cosA = dot(d, L);
                float angle = acos(clamp(cosA, -1.0, 1.0));
                float disc = 1.0 - smoothstep(_SunSize - _SunEdge, _SunSize, angle);
                float glow = pow(saturate(cosA), _SunGlowFalloff) * _SunGlow;
                float haze = pow(saturate(cosA), 4.0) * _SunHaze * (1.0 - saturate(up * 2.5));

                // ---- bulutlar (sadece ufkun üstünde, kubbe izdüşümü)
                float cloudMask = 0.0;
                half3 cloudCol = 0;
                if (y > 0.0)
                {
                    float2 uv = d.xz / (y + 0.18) * (_CloudScale * 0.35);
                    uv += _Time.y * _CloudSpeed * float2(1.0, 0.35);
                    float n = fbm(uv);
                    // güneşe doğru kaydırılmış örnek: yoğunluk farkı = basit aydınlatma
                    float nToSun = fbm(uv + normalize(L.xz + 1e-4) * 0.06);
                    float lit = saturate(0.55 + (n - nToSun) * 9.0);
                    lit = smoothstep(0.25, 0.75, lit); // stilize: iki tonlu gölge

                    cloudMask = smoothstep(_CloudCoverage, _CloudCoverage + _CloudSoftness, n);
                    cloudMask *= smoothstep(0.0, _CloudHeightFade, y) * _CloudOpacity;

                    cloudCol = lerp(_CloudShadowColor.rgb, _CloudColor.rgb, lit);
                    // güneşin yakınındaki bulut kenarları sıcak parlar
                    float edge = 1.0 - smoothstep(_CloudCoverage, _CloudCoverage + _CloudSoftness * 4.0, n);
                    cloudCol += _CloudRimColor.rgb * pow(saturate(cosA), 10.0) * (0.35 + edge * 0.9);
                    // ufka yakın bulutlar sisle karışsın
                    cloudCol = lerp(_HorizonColor.rgb, cloudCol, saturate(y * 6.0 + 0.25));
                }

                col += _SunGlowColor.rgb * (glow + haze);
                col += _SunColor.rgb * disc * (1.0 - cloudMask); // bulut güneşi örter
                col = lerp(col, cloudCol + _SunGlowColor.rgb * glow * 0.35, cloudMask);

                return half4(col, 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
