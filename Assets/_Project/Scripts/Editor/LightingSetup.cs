using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace LandFlipper.EditorTools
{
    /// <summary>
    /// Stylized / low poly görünüm için ışık, gökyüzü ve post-processing kurulumu.
    /// Hedef: sıcak öğleden sonra ışığı, canlı ama yumuşak renkler, okunaklı faceted yüzeyler.
    /// Tüm değerler tek yerde; tekrar çalıştırmak güvenlidir.
    /// </summary>
    public static class LightingSetup
    {
        const string SettingsFolder = "Assets/_Project/Settings";
        const string ProfilePath = SettingsFolder + "/LF_StylizedVolume.asset";
        const string SkyboxPath = "Assets/_Project/Art/Materials/Sky/M_Skybox_Stylized.mat";
        const string SkyShaderName = "LandFlipper/StylizedSky";
        const string SunFlarePath = SettingsFolder + "/LF_SunFlare.asset";

        // Ortak renk paleti
        static readonly Color SunColor = new Color(1f, 0.9f, 0.76f);
        static readonly Color SkyTint = new Color(0.52f, 0.66f, 0.86f);
        static readonly Color HorizonFog = new Color(0.80f, 0.86f, 0.88f);

        // ------------------------------------------------------------ public

        [MenuItem("Land Flipper/Setup/Apply Lighting To Open Scene")]
        public static void ApplyToOpenScene()
        {
            ConfigurePipeline();
            var profile = CreateProfile();
            var sky = CreateSkybox();
            CreateSunFlare();
            var sun = Object.FindFirstObjectByType<Light>();
            ApplySceneLighting(sun, sky);
            AddGlobalVolume(profile);
            foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
                ConfigureCamera(cam);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        }

        public static void ConfigurePipeline()
        {
            var urp = (QualitySettings.renderPipeline ?? GraphicsSettings.defaultRenderPipeline) as UniversalRenderPipelineAsset;
            if (urp == null) return;

            // Optimize: 3 kademe / 70 m / 2048 birinci şahıs kamerada 4×4096 ile neredeyse aynı görünür, çok daha ucuz
            urp.shadowDistance = 70f;
            urp.shadowCascadeCount = 3;
            urp.supportsHDR = true;
            urp.supportsCameraDepthTexture = true; // güneş parlamasının ağaç/arazi arkasında kalması (occlusion)

            var so = new SerializedObject(urp);
            SetFloat(so, "m_MainLightShadowmapResolution", 2048);
            SetBool(so, "m_SoftShadowsSupported", true);
            SetFloat(so, "m_SoftShadowQuality", 2); // orta
            SetBool(so, "m_SupportDataDrivenLensFlare", true);
            // tek bake'li yansıma probu: kutu izdüşümü ile tezgâh/örs/taşlarda doğru konumlu hafif yansıma
            SetBool(so, "m_ReflectionProbeBlending", true);
            SetBool(so, "m_ReflectionProbeBoxProjection", true);
            so.ApplyModifiedPropertiesWithoutUndo();

            // SSAO: low poly yüzeylerin birleşim yerlerinde yumuşak derinlik
            var list = so.FindProperty("m_RendererDataList");
            for (int i = 0; i < list.arraySize; i++)
            {
                var data = list.GetArrayElementAtIndex(i).objectReferenceValue as ScriptableRendererData;
                if (data == null) continue;
                foreach (var feature in data.rendererFeatures)
                {
                    if (feature == null || feature.GetType().Name != "ScreenSpaceAmbientOcclusion") continue;
                    var fso = new SerializedObject(feature);
                    SetFloat(fso, "m_Settings.Intensity", 1.3f);
                    SetFloat(fso, "m_Settings.Radius", 0.35f);
                    SetFloat(fso, "m_Settings.DirectLightingStrength", 0.25f);
                    fso.ApplyModifiedPropertiesWithoutUndo();
                    feature.SetActive(true);
                }
                EditorUtility.SetDirty(data);
            }
            EditorUtility.SetDirty(urp);
        }

        public static VolumeProfile CreateProfile()
        {
            LowPolyWorldBuilder.EnsureFolder(SettingsFolder);
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, ProfilePath);
            }

            // Eski override'ları temizle (alt asset'ler dahil)
            foreach (var component in new System.Collections.Generic.List<VolumeComponent>(profile.components))
            {
                profile.components.Remove(component);
                Object.DestroyImmediate(component, true);
            }

            // Renkleri koruyan tonemapping (ACES stylized renkleri soldurur)
            var tone = Add<Tonemapping>(profile);
            tone.mode.Override(TonemappingMode.Neutral);

            var color = Add<ColorAdjustments>(profile);
            // stylized realism: canlı ama bağırmayan renkler
            color.postExposure.Override(0.08f);
            color.contrast.Override(13f);
            color.saturation.Override(5f);
            color.colorFilter.Override(new Color(1f, 0.985f, 0.95f));

            var white = Add<WhiteBalance>(profile);
            white.temperature.Override(4f);
            white.tint.Override(2f);

            // sıcak ışıklar, hafif serin gölgeler
            var smh = Add<ShadowsMidtonesHighlights>(profile);
            smh.shadows.Override(new Vector4(0.94f, 0.98f, 1.08f, 0f));
            smh.midtones.Override(new Vector4(1f, 1f, 1f, 0f));
            smh.highlights.Override(new Vector4(1.04f, 1.01f, 0.95f, 0f));

            var bloom = Add<Bloom>(profile);
            bloom.threshold.Override(1.0f);
            bloom.intensity.Override(0.4f);
            bloom.scatter.Override(0.65f);
            bloom.tint.Override(new Color(1f, 0.93f, 0.82f));

            var vignette = Add<Vignette>(profile);
            vignette.intensity.Override(0.2f);
            vignette.smoothness.Override(0.45f);
            vignette.color.Override(new Color(0.12f, 0.1f, 0.08f));

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }

        public static Material CreateSkybox()
        {
            LowPolyWorldBuilder.EnsureFolder("Assets/_Project/Art/Materials/Sky");
            var shader = Shader.Find(SkyShaderName);
            var sky = AssetDatabase.LoadAssetAtPath<Material>(SkyboxPath);
            if (sky == null)
            {
                sky = new Material(shader != null ? shader : Shader.Find("Skybox/Procedural"));
                AssetDatabase.CreateAsset(sky, SkyboxPath);
            }

            if (shader == null)
            {
                Debug.LogWarning($"[LandFlipper] {SkyShaderName} bulunamadı, Skybox/Procedural kullanılıyor.");
                sky.shader = Shader.Find("Skybox/Procedural");
                sky.SetColor("_SkyTint", SkyTint);
                sky.SetFloat("_Exposure", 1.15f);
                EditorUtility.SetDirty(sky);
                return sky;
            }

            // Stilize gök: canlı mavi tepe, sisle aynı renkte ufuk, sıcak güneş, kabarık bulutlar
            sky.shader = shader;
            sky.SetColor("_ZenithColor", new Color(0.27f, 0.50f, 0.86f));
            sky.SetColor("_SkyColor", new Color(0.50f, 0.72f, 0.96f));
            sky.SetColor("_HorizonColor", HorizonFog);
            sky.SetColor("_GroundColor", new Color(0.62f, 0.70f, 0.62f));
            sky.SetFloat("_HorizonHeight", 0.22f);
            sky.SetFloat("_ZenithHeight", 0.75f);
            sky.SetColor("_SunColor", new Color(4.2f, 3.9f, 3.3f));
            sky.SetFloat("_SunSize", 0.045f);
            sky.SetFloat("_SunEdge", 0.006f);
            sky.SetColor("_SunGlowColor", new Color(1f, 0.84f, 0.6f));
            sky.SetFloat("_SunGlow", 0.6f);
            sky.SetFloat("_SunGlowFalloff", 18f);
            sky.SetFloat("_SunHaze", 0.35f);
            sky.SetColor("_CloudColor", Color.white);
            sky.SetColor("_CloudShadowColor", new Color(0.72f, 0.79f, 0.90f));
            sky.SetColor("_CloudRimColor", new Color(1f, 0.88f, 0.68f));
            sky.SetFloat("_CloudCoverage", 0.52f);
            sky.SetFloat("_CloudSoftness", 0.035f);
            sky.SetFloat("_CloudScale", 1.5f);
            sky.SetFloat("_CloudSpeed", 0.0045f);
            sky.SetFloat("_CloudOpacity", 0.95f);
            sky.SetFloat("_CloudHeightFade", 0.12f);
            EditorUtility.SetDirty(sky);
            return sky;
        }

        /// <summary>Güneş, gökyüzü, ortam ışığı ve sis (sahne bazlı RenderSettings).</summary>
        public static void ApplySceneLighting(Light sun, Material skybox)
        {
            skybox = AssetDatabase.LoadAssetAtPath<Material>(SkyboxPath) ?? skybox; // taze referans
            if (sun != null)
            {
                sun.type = LightType.Directional;
                sun.color = SunColor;
                sun.intensity = 1.75f;
                sun.shadows = LightShadows.Soft;
                sun.shadowStrength = 0.78f;
                sun.shadowBias = 0.04f;
                sun.shadowNormalBias = 0.35f;
                // alçak, sıcak öğleden sonra açısı: uzun yumuşak gölgeler
                sun.transform.rotation = Quaternion.Euler(36f, -42f, 0f);
                RenderSettings.sun = sun;
                AddSunFlare(sun);
            }

            if (skybox != null) RenderSettings.skybox = skybox;

            RenderSettings.ambientMode = AmbientMode.Trilight;
            // gökten serin mavi, ufuktan sıcak yeşil sekme (çimden yansıyan ışık), yerden toprak
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.72f, 0.88f);
            RenderSettings.ambientEquatorColor = new Color(0.68f, 0.70f, 0.56f);
            RenderSettings.ambientGroundColor = new Color(0.40f, 0.36f, 0.26f);
            RenderSettings.ambientIntensity = 1f;

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = HorizonFog;
            RenderSettings.fogStartDistance = 35f;
            RenderSettings.fogEndDistance = 170f;

            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.reflectionIntensity = 0.75f;
            RenderSettings.defaultReflectionResolution = 128;
        }

        // ------------------------------------------------------------ yansıma

        /// <summary>
        /// Araziyi kapsayan tek yansıma probu. Kurulumda bir kez bake edilir (Custom doku), çalışma
        /// zamanında hiç render maliyeti yoktur. Kutu izdüşümü ile yansımalar konuma oturur.
        /// </summary>
        public static ReflectionProbe AddReflectionProbe(Vector3 center, Vector3 size, string texturePath)
        {
            var go = new GameObject("Reflection Probe (Plot)");
            go.transform.position = center;
            var probe = go.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Custom;
            probe.size = size;
            probe.boxProjection = true;
            probe.resolution = 128;
            probe.hdr = true;
            probe.importance = 1;
            probe.intensity = 1f;
            probe.blendDistance = 2f;
            probe.nearClipPlane = 0.3f;
            probe.farClipPlane = 250f;
            probe.clearFlags = ReflectionProbeClearFlags.Skybox;
            probe.shadowDistance = 60f;

            LowPolyWorldBuilder.EnsureFolder(System.IO.Path.GetDirectoryName(texturePath).Replace('\\', '/'));
            if (Lightmapping.BakeReflectionProbe(probe, texturePath))
            {
                AssetDatabase.ImportAsset(texturePath);
                probe.customBakedTexture = AssetDatabase.LoadAssetAtPath<Texture>(texturePath);
            }
            else Debug.LogWarning("[LandFlipper] Yansıma probu bake edilemedi; gökyüzü yansıması kullanılacak.");
            return probe;
        }

        /// <summary>
        /// Yüzeylere hafif, malzemeye göre parlaklık: metal örs/mengene belirgin, taş ve yaprakta yumuşak
        /// bir ışıltı, ahşapta çok az. Gerçekçi değil "stylized realism": parlama noktaları küçük ve sıcak.
        /// </summary>
        public static void TuneSurface(Material m, float smoothness, float metallic = 0f, bool highlights = true)
        {
            if (m == null) return;
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_SpecularHighlights", highlights ? 1f : 0f);
            if (highlights) m.DisableKeyword("_SPECULARHIGHLIGHTS_OFF");
            else m.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            m.SetFloat("_EnvironmentReflections", 1f);
            m.DisableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            EditorUtility.SetDirty(m);
        }

        // ------------------------------------------------------------ güneş parlaması

        /// <summary>
        /// Güneşe bakınca hafif lens parlaması: güneşin etrafında sıcak bir hale, ince yatay ışık çizgisi
        /// ve ekran ortasına doğru birkaç soluk altıgen "hayalet". Ağaç/arazi arkasında kalınca söner.
        /// </summary>
        public static LensFlareDataSRP CreateSunFlare()
        {
            LowPolyWorldBuilder.EnsureFolder(SettingsFolder);
            var data = AssetDatabase.LoadAssetAtPath<LensFlareDataSRP>(SunFlarePath);
            if (data == null)
            {
                data = ScriptableObject.CreateInstance<LensFlareDataSRP>();
                AssetDatabase.CreateAsset(data, SunFlarePath);
            }

            var elements = new System.Collections.Generic.List<LensFlareDataElementSRP>
            {
                // güneşin etrafındaki yumuşak sıcak hale
                FlareCircle(0f, 0.32f, 5.5f, new Color(1f, 0.86f, 0.62f), 0.12f, 1f),
                // ince yatay ışık çizgisi
                FlareCircle(0f, 0.12f, 1f, new Color(1f, 0.92f, 0.8f), 0.6f, 1f, new Vector2(14f, 0.18f)),
                // güneş çevresinde çok soluk halka
                FlareRing(0f, 0.05f, 7f, new Color(1f, 0.9f, 0.75f)),
                // ekran ortasına doğru soluk hayaletler
                FlareGhost(0.55f, 0.07f, 0.55f, new Color(1f, 0.85f, 0.55f)),
                FlareGhost(0.85f, 0.05f, 0.35f, new Color(0.65f, 1f, 0.75f)),
                FlareGhost(1.25f, 0.06f, 0.8f, new Color(0.6f, 0.8f, 1f)),
                FlareGhost(1.6f, 0.04f, 1.25f, new Color(1f, 0.75f, 0.6f)),
                FlareCircle(1.95f, 0.05f, 0.3f, new Color(0.8f, 0.9f, 1f), 0.5f, 0.6f),
            };
            data.elements = elements.ToArray();
            EditorUtility.SetDirty(data);
            return data;
        }

        static LensFlareDataElementSRP FlareBase(float position, float intensity, float scale, Color tint)
        {
            var e = new LensFlareDataElementSRP();
            e.visible = true;
            e.position = position;
            e.localIntensity = intensity;
            e.uniformScale = scale;
            e.sizeXY = Vector2.one;
            e.tint = tint;
            e.modulateByLightColor = true;
            e.blendMode = SRPLensFlareBlendMode.Additive;
            e.allowMultipleElement = false;
            e.count = 1;
            e.preserveAspectRatio = false;
            e.autoRotate = false;
            return e;
        }

        static LensFlareDataElementSRP FlareCircle(float position, float intensity, float scale, Color tint, float fallOff, float gradient,
            Vector2? size = null)
        {
            var e = FlareBase(position, intensity, scale, tint);
            e.flareType = SRPLensFlareType.Circle;
            e.fallOff = fallOff;
            e.edgeOffset = gradient;
            if (size.HasValue) e.sizeXY = size.Value;
            return e;
        }

        static LensFlareDataElementSRP FlareRing(float position, float intensity, float scale, Color tint)
        {
            var e = FlareBase(position, intensity, scale, tint);
            e.flareType = SRPLensFlareType.Ring;
            e.ringThickness = 0.12f;
            e.fallOff = 0.5f;
            e.edgeOffset = 0.4f;
            return e;
        }

        static LensFlareDataElementSRP FlareGhost(float position, float intensity, float scale, Color tint)
        {
            var e = FlareBase(position, intensity, scale, tint);
            e.flareType = SRPLensFlareType.Polygon;
            e.sideCount = 6;
            e.sdfRoundness = 0.35f;
            e.fallOff = 0.85f;
            e.edgeOffset = 0.25f;
            return e;
        }

        static void AddSunFlare(Light sun)
        {
            var data = AssetDatabase.LoadAssetAtPath<LensFlareDataSRP>(SunFlarePath);
            if (data == null) data = CreateSunFlare();

            var flare = sun.GetComponent<LensFlareComponentSRP>();
            if (flare == null) flare = sun.gameObject.AddComponent<LensFlareComponentSRP>();
            flare.lensFlareData = data;
            flare.intensity = 1f;
            flare.scale = 1f;
            flare.attenuationByLightShape = false; // yönlü ışık
            flare.useOcclusion = true;              // ağaç/tepe arkasında kalınca söner
            flare.occlusionRadius = 0.6f;
            flare.sampleCount = 24;
            flare.occlusionOffset = 0.05f;
            flare.allowOffScreen = false;
            EditorUtility.SetDirty(flare);
        }

        public static void AddGlobalVolume(VolumeProfile profile)
        {
            profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath) ?? profile; // taze referans
            var existing = Object.FindFirstObjectByType<Volume>();
            var go = existing != null ? existing.gameObject : new GameObject("Global Volume (Stylized)");
            var volume = existing != null ? existing : go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 0f;
            volume.sharedProfile = profile;
        }

        public static void ConfigureCamera(Camera cam)
        {
            if (cam == null) return;
            var data = cam.GetComponent<UniversalAdditionalCameraData>();
            if (data == null) data = cam.gameObject.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.antialiasingQuality = AntialiasingQuality.High;
            cam.allowHDR = true;
            cam.farClipPlane = 400f;
        }

        // ------------------------------------------------------------ helpers

        static T Add<T>(VolumeProfile profile) where T : VolumeComponent
        {
            var component = profile.Add<T>(true);
            component.name = typeof(T).Name;
            component.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(component, profile);
            return component;
        }

        static void SetFloat(SerializedObject so, string path, float value)
        {
            var p = so.FindProperty(path);
            if (p == null) return;
            if (p.propertyType == SerializedPropertyType.Integer || p.propertyType == SerializedPropertyType.Enum) p.intValue = (int)value;
            else p.floatValue = value;
        }

        static void SetBool(SerializedObject so, string path, bool value)
        {
            var p = so.FindProperty(path);
            if (p != null) p.boolValue = value;
        }
    }
}
