using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.IO;
using System.Collections.Generic;
using System.Text.RegularExpressions;

public class LightShadowProBaker : EditorWindow
{
    [MenuItem("Tools/Pro Baker")]
    public static void ShowWindow()
    {
        GetWindow<LightShadowProBaker>("Light Shadow Pro Baker").minSize = new Vector2(400, 750);
    }

    // --- 烘焙列表 ---
    [SerializeField] private List<GameObject> targetObjects = new List<GameObject>();
    private Vector2 scrollPosition;

    private int selectedTab = 0;
    private string[] tabs = { "Diffuse", "Standard" };

    // --- 核心参数 ---
    private int atlasSize = 4096;
    private int perObjectSize = 2048;
    private bool useSuperSampling = true;

    // 控制是否重排UV
    private bool repackUVs = true;

    // --- 光照参数 (已校准为旧版效果) ---
    private float brightness = 1.3f;
    private float shadowLift = 0.2f;
    private float gammaCorrection = 0.6f;
    private float normalStrength = 0.0f;

    // PBR 参数
    private float pbrBrightness = 1.0f;
    private float pbrShadowLift = 0.0f;
    private float metallicMult = 1.0f;
    private float smoothnessMult = 1.0f;

    // UV 参数 (这些就是你刚才提到的)
    private float unwrapHardAngle = 88.0f;
    private float unwrapPackMargin = 8.0f;

    private bool autoHideOriginals = true;

    private void OnGUI()
    {
        GUILayout.Space(10);
        selectedTab = GUILayout.Toolbar(selectedTab, tabs, GUILayout.Height(30));

        if (selectedTab == 0)
        {
            EditorGUILayout.HelpBox("适合非金属物体: 将光照、颜色、法线细节合并为一张图。", MessageType.Info);
        }
        else
        {
            EditorGUILayout.HelpBox("适合金属、反光物体: 将 Standard 材质信息全部烘焙进一张图。", MessageType.Info);
        }

        // --- 列表区域 ---
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        GUILayout.Label($"待烘焙物体 ({targetObjects.Count})", EditorStyles.boldLabel);

        Rect dropArea = GUILayoutUtility.GetRect(0.0f, 60.0f, GUILayout.ExpandWidth(true));

        GUIStyle bigStyle = new GUIStyle(EditorStyles.helpBox);
        bigStyle.fontSize = 18;
        bigStyle.alignment = TextAnchor.MiddleCenter;
        bigStyle.fontStyle = FontStyle.Bold;
        bigStyle.normal.textColor = Color.white;

        Color originalBgColor = GUI.backgroundColor;
        GUI.backgroundColor = new Color(0.1f, 0.9f, 0.1f, 1f); // 绿色背景

        GUI.Box(dropArea, "=== 拖拽物体到此 ===", bigStyle);

        GUI.backgroundColor = originalBgColor;

        HandleDragAndDrop(dropArea);

        if (GUILayout.Button("清空列表")) targetObjects.Clear();

        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.Height(100));
        for (int i = 0; i < targetObjects.Count; i++)
        {
            EditorGUILayout.BeginHorizontal();
            targetObjects[i] = (GameObject)EditorGUILayout.ObjectField(targetObjects[i], typeof(GameObject), true);
            if (GUILayout.Button("X", GUILayout.Width(20))) { targetObjects.RemoveAt(i); i--; }
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();

        GUILayout.Space(10);

        // --- 核心设置 ---
        EditorGUILayout.LabelField("通用设置", EditorStyles.boldLabel);

        repackUVs = EditorGUILayout.ToggleLeft(" 重新打包 UV (多物体拼图时必须勾选)", repackUVs);
        if (!repackUVs)
        {
            EditorGUILayout.HelpBox("【单体模式】：将保持原始 UV 和 Mesh。", MessageType.Info);
        }

        atlasSize = EditorGUILayout.IntPopup("最终图集大小", atlasSize, new string[] { "1024", "2048", "4096", "8192" }, new int[] { 1024, 2048, 4096, 8192 });
        perObjectSize = EditorGUILayout.IntPopup("单物体精度", perObjectSize, new string[] { "512", "1024", "2048", "4096" }, new int[] { 512, 1024, 2048, 4096 });
        int _horNum = (int)(atlasSize / perObjectSize);
        GUILayout.Label(_horNum + "x" + _horNum + "="+ _horNum* _horNum, EditorStyles.miniLabel);

        useSuperSampling = EditorGUILayout.Toggle("开启超采样 (SSAA)", useSuperSampling);

        GUILayout.Space(10);

        if (selectedTab == 0) // Diffuse
        {
            EditorGUILayout.LabelField("Diffuse 光照调节", EditorStyles.boldLabel);
            brightness = EditorGUILayout.Slider("亮度倍率 (Boost)", brightness, 0.5f, 3.0f);
            gammaCorrection = EditorGUILayout.Slider("对比度/Gamma (0.6最佳)", gammaCorrection, 0.1f, 1.5f);
            shadowLift = EditorGUILayout.Slider("暗部提亮 (Lift)", shadowLift, 0.0f, 0.5f);
            normalStrength = EditorGUILayout.Slider("法线细节 (默认0)", normalStrength, 0.0f, 2.0f);
        }
        else // PBR
        {
            EditorGUILayout.LabelField("PBR 材质微调", EditorStyles.boldLabel);
            pbrBrightness = EditorGUILayout.Slider("总亮度倍率", pbrBrightness, 0.5f, 3.0f);
            pbrShadowLift = EditorGUILayout.Slider("暗部提亮", pbrShadowLift, 0.0f, 0.5f);
            metallicMult = EditorGUILayout.Slider("金属度增强", metallicMult, 0.0f, 2.0f);
            smoothnessMult = EditorGUILayout.Slider("光滑度增强", smoothnessMult, 0.0f, 2.0f);
        }

        // 【核心加回】：UV 设置和隐藏原物体选项
        GUILayout.Space(10);
        EditorGUILayout.LabelField("UV 设置", EditorStyles.boldLabel);
        // 只有重排UV时，硬边角度才有用，所以置灰或者隐藏逻辑可以加上，但为了方便你调整，我直接显示
        GUI.enabled = repackUVs;
        unwrapHardAngle = EditorGUILayout.Slider("UV硬边角度", unwrapHardAngle, 10f, 180f);
        unwrapPackMargin = EditorGUILayout.Slider("内部UV间隙", unwrapPackMargin, 2f, 32f);
        GUI.enabled = true;

        GUILayout.Space(5);
        autoHideOriginals = EditorGUILayout.Toggle("完成后隐藏原物体", autoHideOriginals);

        GUILayout.FlexibleSpace();
        GUILayout.Space(10);

        GUI.enabled = targetObjects.Count > 0;

        Color defaultColor = GUI.backgroundColor;
        if (selectedTab == 0)
        {
            GUI.backgroundColor = new Color(0.6f, 1f, 0.6f);
        }
        else
        {
            GUI.backgroundColor = new Color(0.6f, 0.8f, 1f);
        }

        string modeName = selectedTab == 0 ? "Diffuse" : "Standard";
        if (GUILayout.Button($"【开始烘焙】{modeName} 模式 ({targetObjects.Count} 个物体)", GUILayout.Height(50)))
        {
            targetObjects.RemoveAll(x => x == null);
            if (targetObjects.Count == 0)
                EditorUtility.DisplayDialog("提示", "列表为空", "确定");
            else
                StartBakingProcess();
        }

        GUI.backgroundColor = defaultColor;
        GUI.enabled = true;
        GUILayout.Space(5); // 底部留白
        
        GUI.enabled = false;
        GUILayout.Label("V1.0.0.1 (2025.12.29)", EditorStyles.miniLabel);
        GUI.enabled = true;
        GUILayout.Space(5); // 底部留白
    }

    private void HandleDragAndDrop(Rect dropArea)
    {
        Event evt = Event.current;
        switch (evt.type)
        {
            case EventType.DragUpdated:
            case EventType.DragPerform:
                if (!dropArea.Contains(evt.mousePosition)) return;
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                if (evt.type == EventType.DragPerform)
                {
                    DragAndDrop.AcceptDrag();
                    bool changed = false;
                    foreach (Object obj in DragAndDrop.objectReferences)
                    {
                        if (obj is GameObject go && !targetObjects.Contains(go))
                        {
                            targetObjects.Add(go);
                            changed = true;
                        }
                    }
                    if (changed) { evt.Use(); GUIUtility.ExitGUI(); }
                }
                break;
        }
    }

    private string GetAutoSavePath()
    {
        var scene = EditorSceneManager.GetActiveScene();
        string timeStamp = System.DateTime.Now.ToString("MMdd_HHmm");
        if (string.IsNullOrEmpty(scene.path)) return "Assets/Temp_BakedAtlas_" + timeStamp;
        string sceneDir = Path.GetDirectoryName(scene.path);
        string sceneName = Path.GetFileNameWithoutExtension(scene.path);
        return Path.Combine(sceneDir, $"{sceneName}_BakedAtlas_{timeStamp}").Replace("\\", "/");
    }

    private void StartBakingProcess()
    {
        GameObject[] targets = targetObjects.ToArray();
        string saveDir = GetAutoSavePath();
        if (!Directory.Exists(saveDir)) Directory.CreateDirectory(saveDir);

        CreateBakeShaders();
        AssetDatabase.Refresh();

        try
        {
            List<Texture2D> bakedTextures = new List<Texture2D>();
            List<GameObject> processedObjects = new List<GameObject>();
            List<Mesh> meshCopies = new List<Mesh>();

            for (int i = 0; i < targets.Length; i++)
            {
                GameObject go = targets[i];
                if (!go) continue;
                MeshRenderer mr = go.GetComponent<MeshRenderer>();
                MeshFilter mf = go.GetComponent<MeshFilter>();
                if (!mr || !mf) continue;

                EditorUtility.DisplayProgressBar("Baking", $"处理中: {go.name}", (float)i / targets.Length);

                Mesh meshCopy = Instantiate(mf.sharedMesh);
                string safeName = SanitizeFileName(go.name);
                meshCopy.name = safeName + "_BakedMesh";

                if (repackUVs)
                {
                    GenerateCompactUVSafe(meshCopy);
                }
                else
                {
                    PrepareOriginalUVs(meshCopy);
                }

                Texture2D bakedTex = RenderObjectToTexture(go, meshCopy, mr, selectedTab == 1);
                bakedTex.name = safeName + "_Tex";

                bakedTextures.Add(bakedTex);
                processedObjects.Add(go);
                meshCopies.Add(meshCopy);
            }

            Texture2D atlas;
            Rect[] uvs;

            if (repackUVs)
            {
                atlas = new Texture2D(atlasSize, atlasSize, TextureFormat.RGB24, false);
                uvs = atlas.PackTextures(bakedTextures.ToArray(), 0, atlasSize);
            }
            else
            {
                atlas = bakedTextures[0];
                uvs = new Rect[1];
                uvs[0] = new Rect(0, 0, 1, 1);
            }

            if (uvs == null) { EditorUtility.DisplayDialog("错误", "图集装不下！", "确定"); return; }

            byte[] atlasBytes = atlas.EncodeToPNG();
            string atlasPath = saveDir + (repackUVs ? "/Atlas_Main.png" : $"/{processedObjects[0].name}_Baked.png");
            File.WriteAllBytes(atlasPath, atlasBytes);

            if (repackUVs) foreach (var tex in bakedTextures) DestroyImmediate(tex);
            AssetDatabase.Refresh();

            TextureImporter importer = AssetImporter.GetAtPath(atlasPath) as TextureImporter;
            if (importer != null)
            {
                importer.sRGBTexture = true;
                importer.filterMode = FilterMode.Bilinear;
                importer.anisoLevel = 8;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.SaveAndReimport();
            }

            Texture2D savedAtlas = AssetDatabase.LoadAssetAtPath<Texture2D>(atlasPath);
            Material atlasMat = new Material(Shader.Find("Unlit/Texture"));
            atlasMat.mainTexture = savedAtlas;
            string matPath = saveDir + (repackUVs ? "/Atlas_Mat.mat" : $"/{processedObjects[0].name}_Mat.mat");
            AssetDatabase.CreateAsset(atlasMat, matPath);

            GameObject outputRoot = new GameObject("Baked_Scene_Root_" + System.DateTime.Now.ToString("HHmm"));
            if (targets.Length > 0 && targets[0] != null) outputRoot.transform.position = Vector3.zero;

            for (int i = 0; i < processedObjects.Count; i++)
            {
                if (!repackUVs && i > 0) break;

                GameObject originalGO = processedObjects[i];
                string safeName = SanitizeFileName(originalGO.name);

                Mesh finalMesh = null;

                if (repackUVs)
                {
                    Mesh mesh = meshCopies[i];
                    Rect rect = uvs[i];
                    Vector2[] currentUVs = mesh.uv;
                    Vector2[] atlasUVs = new Vector2[currentUVs.Length];
                    for (int k = 0; k < currentUVs.Length; k++)
                    {
                        atlasUVs[k] = new Vector2(
                            currentUVs[k].x * rect.width + rect.x,
                            currentUVs[k].y * rect.height + rect.y
                        );
                    }
                    mesh.uv = atlasUVs;
                    mesh.uv2 = null; mesh.uv3 = null; mesh.colors = null; mesh.tangents = null;

                    string meshPath = saveDir + "/" + safeName + "_Mesh.asset";
                    AssetDatabase.CreateAsset(mesh, meshPath);
                    finalMesh = mesh;
                }
                else
                {
                    DestroyImmediate(meshCopies[i]);
                    finalMesh = originalGO.GetComponent<MeshFilter>().sharedMesh;
                }

                GameObject newGO = Instantiate(originalGO, outputRoot.transform);
                newGO.name = originalGO.name + "_Baked";
                newGO.transform.position = originalGO.transform.position;
                newGO.transform.rotation = originalGO.transform.rotation;
                newGO.transform.localScale = originalGO.transform.localScale;

                newGO.GetComponent<MeshFilter>().sharedMesh = finalMesh;
                newGO.GetComponent<MeshRenderer>().sharedMaterial = atlasMat;

                if (autoHideOriginals) { Undo.RecordObject(originalGO, "Hide Original"); originalGO.SetActive(false); }
            }

            Debug.Log($"烘焙完成！保存路径: {saveDir}");
            Selection.activeGameObject = outputRoot;
        }
        catch (System.Exception e)
        {
            Debug.LogError("错误: " + e.ToString());
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            AssetDatabase.Refresh();
        }
    }

    private void GenerateCompactUVSafe(Mesh mesh)
    {
        UnwrapParam param = new UnwrapParam();
        UnwrapParam.SetDefaults(out param);
        param.hardAngle = unwrapHardAngle;
        param.packMargin = unwrapPackMargin / (float)perObjectSize;

        List<Vector2> uvOriginal = new List<Vector2>(); mesh.GetUVs(0, uvOriginal);
        List<Vector2> uvLightmap = new List<Vector2>(); mesh.GetUVs(1, uvLightmap);
        if (uvLightmap.Count > 0) mesh.SetUVs(2, uvLightmap);

        Unwrapping.GenerateSecondaryUVSet(mesh, param);

        List<Vector2> compactUVs = new List<Vector2>(); mesh.GetUVs(1, compactUVs);
        List<Vector2> splitOriginalUVs = new List<Vector2>(); mesh.GetUVs(0, splitOriginalUVs);

        mesh.SetUVs(0, compactUVs);
        mesh.SetUVs(1, splitOriginalUVs);
    }

    private void PrepareOriginalUVs(Mesh mesh)
    {
        List<Vector2> originalUV = new List<Vector2>(); mesh.GetUVs(0, originalUV);
        List<Vector2> lightmapUV = new List<Vector2>(); mesh.GetUVs(1, lightmapUV);
        mesh.SetUVs(0, originalUV);
        mesh.SetUVs(1, originalUV);
        mesh.SetUVs(2, lightmapUV);
    }

    private Texture2D RenderObjectToTexture(GameObject go, Mesh mesh, MeshRenderer mr, bool isPBR)
    {
        string shaderName = isPBR ? "Hidden/Mobile-Atlas-PBR-Bake" : "Hidden/Mobile-Atlas-Detail-Bake";
        Material bakeMat = new Material(Shader.Find(shaderName));
        Material originalMat = mr.sharedMaterial;

        bakeMat.SetTexture("_MainTex", originalMat && originalMat.mainTexture ? originalMat.mainTexture : Texture2D.whiteTexture);

        if (isPBR && originalMat && originalMat.HasProperty("_Color"))
            bakeMat.SetColor("_Color", originalMat.GetColor("_Color"));

        if (mr.lightmapIndex >= 0 && mr.lightmapIndex < LightmapSettings.lightmaps.Length)
        {
            bakeMat.SetTexture("_LightMap", LightmapSettings.lightmaps[mr.lightmapIndex].lightmapColor);
            bakeMat.SetVector("_LightMapST", mr.lightmapScaleOffset);
        }
        else
        {
            bakeMat.SetTexture("_LightMap", Texture2D.whiteTexture);
            bakeMat.SetVector("_LightMapST", new Vector4(1, 1, 0, 0));
        }

        if (isPBR)
        {
            if (originalMat)
            {
                if (originalMat.HasProperty("_BumpMap")) bakeMat.SetTexture("_BumpMap", originalMat.GetTexture("_BumpMap"));
                if (originalMat.HasProperty("_MetallicGlossMap")) bakeMat.SetTexture("_MetallicGlossMap", originalMat.GetTexture("_MetallicGlossMap"));
                if (originalMat.HasProperty("_OcclusionMap")) bakeMat.SetTexture("_OcclusionMap", originalMat.GetTexture("_OcclusionMap"));
                if (originalMat.HasProperty("_EmissionMap")) bakeMat.SetTexture("_EmissionMap", originalMat.GetTexture("_EmissionMap"));

                // 【核心修复：检查 Emission】
                if (originalMat.IsKeywordEnabled("_EMISSION") && originalMat.HasProperty("_EmissionColor"))
                {
                    bakeMat.SetColor("_EmitColor", originalMat.GetColor("_EmissionColor"));
                }
                else
                {
                    bakeMat.SetColor("_EmitColor", Color.black);
                }

                bakeMat.SetMatrix("_ObjectMatrix", go.transform.localToWorldMatrix);
            }

            // 抓取反射探针
            Texture reflectionCube = null;
            ReflectionProbe[] probes = FindObjectsOfType<ReflectionProbe>();
            float minDist = float.MaxValue;
            Bounds goBounds = go.GetComponent<Renderer>().bounds;

            foreach (var probe in probes)
            {
                if (goBounds.Intersects(probe.bounds))
                {
                    float dist = Vector3.Distance(go.transform.position, probe.transform.position);
                    if (dist < minDist)
                    {
                        minDist = dist;
                        reflectionCube = probe.texture;
                    }
                }
            }
            if (reflectionCube == null) reflectionCube = ReflectionProbe.defaultTexture;

            if (reflectionCube != null) bakeMat.SetTexture("_ReflectionCube", reflectionCube);

            bakeMat.SetFloat("_Brightness", pbrBrightness);
            bakeMat.SetFloat("_ShadowLift", pbrShadowLift);
            bakeMat.SetFloat("_MetallicMult", metallicMult);
            bakeMat.SetFloat("_SmoothnessMult", smoothnessMult);
        }
        else
        {
            if (originalMat && originalMat.HasProperty("_BumpMap"))
                bakeMat.SetTexture("_BumpMap", originalMat.GetTexture("_BumpMap"));

            bakeMat.SetFloat("_NormalStrength", normalStrength);
            bakeMat.SetFloat("_Brightness", brightness);
            bakeMat.SetFloat("_ShadowLift", shadowLift);
            bakeMat.SetFloat("_Gamma", gammaCorrection);
        }

        int renderSize = useSuperSampling ? perObjectSize * 2 : perObjectSize;
        RenderTexture rt = RenderTexture.GetTemporary(renderSize, renderSize, 0, RenderTextureFormat.ARGB32);
        Graphics.SetRenderTarget(rt);
        GL.Clear(true, true, new Color(0, 0, 0, 0));

        GL.PushMatrix();
        GL.LoadOrtho();
        bakeMat.SetPass(0);
        Graphics.DrawMeshNow(mesh, Matrix4x4.identity);
        GL.PopMatrix();

        if (useSuperSampling)
        {
            RenderTexture finalRT = RenderTexture.GetTemporary(perObjectSize, perObjectSize, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(rt, finalRT);
            RenderTexture.ReleaseTemporary(rt);
            rt = finalRT;
        }

        Texture2D tempRead = new Texture2D(perObjectSize, perObjectSize, TextureFormat.RGBA32, false);
        RenderTexture.active = rt;
        tempRead.ReadPixels(new Rect(0, 0, perObjectSize, perObjectSize), 0, 0);
        tempRead.Apply();

        ApplySolidDilation(tempRead, 32);

        Texture2D result = new Texture2D(perObjectSize, perObjectSize, TextureFormat.RGB24, false);
        result.SetPixels(tempRead.GetPixels());
        result.Apply();

        RenderTexture.ReleaseTemporary(rt);
        DestroyImmediate(bakeMat);
        return result;
    }

    private void ApplySolidDilation(Texture2D tex, int iterations)
    {
        int w = tex.width;
        int h = tex.height;
        Color[] pixels = tex.GetPixels();
        Color[] nextPixels = new Color[pixels.Length];
        System.Array.Copy(pixels, nextPixels, pixels.Length);

        for (int k = 0; k < iterations; k++)
        {
            for (int i = 0; i < pixels.Length; i++)
            {
                if (pixels[i].a < 0.99f)
                {
                    int y = i / w; int x = i % w;
                    if (x > 0 && pixels[i - 1].a > 0.99f) { nextPixels[i] = pixels[i - 1]; continue; }
                    if (x < w - 1 && pixels[i + 1].a > 0.99f) { nextPixels[i] = pixels[i + 1]; continue; }
                    if (y > 0 && pixels[i - w].a > 0.99f) { nextPixels[i] = pixels[i - w]; continue; }
                    if (y < h - 1 && pixels[i + w].a > 0.99f) { nextPixels[i] = pixels[i + w]; continue; }
                }
            }
            System.Array.Copy(nextPixels, pixels, pixels.Length);
        }
        tex.SetPixels(pixels);
        tex.Apply();
    }

    private string SanitizeFileName(string name)
    {
        string invalidChars = System.Text.RegularExpressions.Regex.Escape(new string(System.IO.Path.GetInvalidFileNameChars()));
        string invalidRegStr = string.Format(@"([{0}]*\.+$)|([{0}]+)", invalidChars);
        return System.Text.RegularExpressions.Regex.Replace(name, invalidRegStr, "_");
    }

    private void CreateBakeShaders()
    {
        string commonVert = @"
            struct appdata {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0; 
                float2 uvOriginal : TEXCOORD1; 
                float2 uvLM : TEXCOORD2;
                float3 normal : NORMAL; 
            };
            struct v2f {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 uvLM : TEXCOORD1;
                float3 wNorm : TEXCOORD3;
            };
            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = float4(v.uv.x * 2 - 1, v.uv.y * 2 - 1, 0, 1);
                #if UNITY_UV_STARTS_AT_TOP
                o.vertex.y *= -1;
                #endif
                o.uv = v.uvOriginal; 
                o.uvLM = v.uvLM.xy * _LightMapST.xy + _LightMapST.zw;
                o.wNorm = mul((float3x3)_ObjectMatrix, v.normal);
                return o;
            }";

        // 1. Diffuse Shader 
        string diffuseShader = @"
Shader ""Hidden/Mobile-Atlas-Detail-Bake""
{
    Properties { _MainTex(""T"", 2D)=""white""{} _BumpMap(""N"", 2D)=""bump""{} _LightMap(""L"", 2D)=""white""{} }
    SubShader
    {
        Tags { ""RenderType""=""Opaque"" }
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include ""UnityCG.cginc""
            
            sampler2D _MainTex, _LightMap, _BumpMap;
            float4 _LightMapST;
            float _Brightness, _ShadowLift, _NormalStrength, _Gamma;
            float4x4 _ObjectMatrix;

            " + commonVert + @"
            
            fixed4 frag (v2f i) : SV_Target
            {
                fixed3 col = tex2D(_MainTex, i.uv).rgb;
                
                fixed3 lm = DecodeLightmap(tex2D(_LightMap, i.uvLM));
                lm *= _Brightness;
                lm = max(lm, _ShadowLift);
                lm = pow(lm, _Gamma); 

                fixed3 normal = UnpackNormal(tex2D(_BumpMap, i.uv));
                float3 lightDir = normalize(float3(0.5, 0.8, 1.0));
                float NdotL = dot(normal, lightDir);
                float detail = lerp(1.0, NdotL * 1.5, _NormalStrength); 
                
                fixed3 final = col * lm * detail;
                return fixed4(final, 1.0);
            }
            ENDCG
        }
    }
}";

        // 2. PBR Shader
        string pbrShader = @"
Shader ""Hidden/Mobile-Atlas-PBR-Bake""
{
    Properties { 
        _MainTex(""T"", 2D)=""white""{} 
        _BumpMap(""N"", 2D)=""bump""{} 
        _LightMap(""L"", 2D)=""white""{} 
        _MetallicGlossMap(""M"", 2D)=""white""{}
        _OcclusionMap(""O"", 2D)=""white""{}
        _EmissionMap(""E"", 2D)=""black""{} 
        _ReflectionCube(""Reflection Cubemap"", Cube) = """" {}
    }
    SubShader
    {
        Tags { ""RenderType""=""Opaque"" }
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include ""UnityCG.cginc""
            #include ""UnityStandardBRDF.cginc""
            
            sampler2D _MainTex, _BumpMap, _LightMap, _MetallicGlossMap, _OcclusionMap, _EmissionMap;
            float4 _LightMapST, _Color, _EmitColor;
            float _Brightness, _ShadowLift, _MetallicMult, _SmoothnessMult;
            float4x4 _ObjectMatrix;
            samplerCUBE _ReflectionCube;

            " + commonVert + @"

            fixed4 frag (v2f i) : SV_Target
            {
                fixed3 alb = tex2D(_MainTex, i.uv).rgb;
                #if defined(_Color)
                alb *= _Color.rgb;
                #endif

                fixed3 lm = DecodeLightmap(tex2D(_LightMap, i.uvLM));
                fixed4 ms = tex2D(_MetallicGlossMap, i.uv);
                fixed ao = tex2D(_OcclusionMap, i.uv).g;
                fixed3 em = tex2D(_EmissionMap, i.uv).rgb * _EmitColor.rgb;

                float m = saturate(ms.r * _MetallicMult); 
                float s = saturate(ms.a * _SmoothnessMult); 

                float3 normal = normalize(i.wNorm); 
                float3 reflDir = normal; 
                
                float4 reflSample = texCUBElod(_ReflectionCube, float4(reflDir, (1-s) * 6));
                float3 reflColor = DecodeHDR(reflSample, unity_SpecCube0_HDR);

                float oneMinusReflectivity = 1.0 - m;
                float3 diffuse = alb * oneMinusReflectivity * lm * _Brightness * ao;

                float3 specColor = lerp(float3(0.04, 0.04, 0.04), alb, m);
                float3 specular = reflColor * specColor * s * ao;

                float3 final = diffuse + specular + em;
                final = max(final, alb * _ShadowLift);

                return fixed4(final, 1.0);
            }
            ENDCG
        }
    }
}";

        string diffusePath = "Assets/Mobile-Atlas-Detail-Bake.shader";
        string pbrPath = "Assets/Mobile-Atlas-PBR-Bake.shader";

        File.WriteAllText(diffusePath, diffuseShader);
        File.WriteAllText(pbrPath, pbrShader);
    }
}