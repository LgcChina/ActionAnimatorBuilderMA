#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// 明确引用 MA 命名空间
using nadena.dev.modular_avatar.core;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDK3.Avatars.Components;

public class ActionAnimatorBuilderMA : EditorWindow
{
    private const string BaseFolder = "Assets/LGC/Tools/快速批量动画器构建/已创建动画器";
    private string controllerName = "PoseAnimatorController";
    private string parameterName = "pose";
    private int startIndex = 1;

    // 过渡参数（与Unity官方API完全一致）
    private bool transitionHasExitTime = false;
    private float transitionExitTime = 0f;
    private bool transitionHasFixedDuration = true;
    private float transitionDuration = 0f;

    // 音频功能
    private bool enableAudioFeature = false;
    private List<AnimationAudioPair> animationAudioPairs = new List<AnimationAudioPair>();
    private Vector2 scroll;
    private string statusMessage = "";
    private AnimatorController lastController = null;
    private int lastClipCount = 0;
    private int lastStartIndex = 1;
    private string lastGeneratedFolderPath = "";

    // 菜单按钮命名方式
    private enum NamingStyle
    {
        UseAnimationName,
        UseUniformNaming
    }
    private NamingStyle menuNamingStyle = NamingStyle.UseAnimationName;

    // 动画-音频配对结构
    private class AnimationAudioPair
    {
        public AnimationClip AnimationClip;
        public AudioClip AudioClip;

        public AnimationAudioPair(AnimationClip animClip)
        {
            AnimationClip = animClip;
            AudioClip = null;
        }
    }

    // 修正工具入口
    [MenuItem("LGC/LGC Action 层动画器批量构建（MA 版）")]
    public static void OpenWindow()
    {
        var win = GetWindow<ActionAnimatorBuilderMA>("动画器批量构建（MA 版）");
        win.minSize = new Vector2(950, 600);
        win.position = new Rect(win.position.x, win.position.y, 950, 600);
    }

    private void OnEnable() => EnsureFolder(BaseFolder);

    private void OnGUI()
    {
        EditorGUILayout.LabelField("MA 动画音频工具", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        // 左右布局 - 调整为导入面板2/3，操作面板1/3
        EditorGUILayout.BeginHorizontal();

        // 左侧：动画/音频列表 (占2/3)
        float leftWidth = Mathf.Max(450, position.width * 0.666f);
        EditorGUILayout.BeginVertical(GUILayout.Width(leftWidth), GUILayout.ExpandHeight(true));
        EditorGUILayout.LabelField("动画片段管理", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("当前2.0试用创建增加音频功能", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("在确定一切之前，不要关闭面板", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("做完了记得把控制器和预制件移动到别的地方，防止覆盖", EditorStyles.boldLabel);

        // 拆分拖拽区域：动画拖入 + 音频拖入（仅启用音频时显示）
        DrawSplitDragAreas(leftWidth);
        EditorGUILayout.Space();

        // 清空/排序按钮
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("清空列表", GUILayout.ExpandWidth(false))) animationAudioPairs.Clear();
        GUILayout.Space(5);
        if (GUILayout.Button("按名称排序", GUILayout.ExpandWidth(false)))
        {
            animationAudioPairs = animationAudioPairs
                .Where(p => p.AnimationClip != null)
                .OrderBy(p => p.AnimationClip.name)
                .ToList();
        }
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.Space();

        // 列表区域
        EditorGUILayout.LabelField($"已导入：{animationAudioPairs.Count} 个动画片段", GUILayout.ExpandWidth(true));
        float scrollHeight = position.height - 200;
        scrollHeight = Mathf.Max(300, scrollHeight);

        using (var view = new EditorGUILayout.ScrollViewScope(scroll, GUILayout.Height(scrollHeight), GUILayout.ExpandWidth(true)))
        {
            scroll = view.scrollPosition;

            // 列表表头 - 根据音频绑定状态调整列显示
            EditorGUILayout.BeginHorizontal();
            if (enableAudioFeature)
            {
                EditorGUILayout.LabelField("动画片段", GUILayout.Width(leftWidth * 0.4f));
                EditorGUILayout.LabelField("音频片段", GUILayout.Width(leftWidth * 0.4f));
                EditorGUILayout.LabelField("操作", GUILayout.Width(leftWidth * 0.2f));
            }
            else
            {
                EditorGUILayout.LabelField("动画片段", GUILayout.Width(leftWidth * 0.6f));
                EditorGUILayout.LabelField("操作", GUILayout.Width(leftWidth * 0.4f));
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Separator();

            // 列表项 - 根据音频绑定状态调整列显示和宽度
            for (int i = 0; i < animationAudioPairs.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();

                // 动画片段选择框
                if (enableAudioFeature)
                {
                    animationAudioPairs[i].AnimationClip = (AnimationClip)EditorGUILayout.ObjectField(
                        animationAudioPairs[i].AnimationClip, typeof(AnimationClip), false,
                        GUILayout.Width(leftWidth * 0.4f));

                    // 音频片段选择框（仅启用音频时显示）
                    animationAudioPairs[i].AudioClip = (AudioClip)EditorGUILayout.ObjectField(
                        animationAudioPairs[i].AudioClip, typeof(AudioClip), false,
                        GUILayout.Width(leftWidth * 0.4f));

                    // 移除按钮
                    if (GUILayout.Button("移除", GUILayout.Width(leftWidth * 0.2f)))
                    {
                        animationAudioPairs.RemoveAt(i);
                        i--;
                    }
                }
                else
                {
                    // 关闭音频绑定时，动画列占60%，移除按钮占40%
                    animationAudioPairs[i].AnimationClip = (AnimationClip)EditorGUILayout.ObjectField(
                        animationAudioPairs[i].AnimationClip, typeof(AnimationClip), false,
                        GUILayout.Width(leftWidth * 0.6f));

                    if (GUILayout.Button("移除", GUILayout.Width(leftWidth * 0.4f)))
                    {
                        animationAudioPairs.RemoveAt(i);
                        i--;
                    }
                }

                EditorGUILayout.EndHorizontal();
                EditorGUILayout.Separator();
            }
        }
        EditorGUILayout.EndVertical();

        // 右侧：操作面板 (占1/3)
        float rightWidth = Mathf.Max(250, position.width * 0.333f);
        EditorGUILayout.BeginVertical(GUILayout.Width(rightWidth), GUILayout.ExpandHeight(true));

        // 基础设置
        EditorGUILayout.BeginVertical("box", GUILayout.ExpandWidth(true));
        EditorGUILayout.LabelField("基础设置", EditorStyles.boldLabel);
        controllerName = EditorGUILayout.TextField("控制器名称", controllerName);
        parameterName = EditorGUILayout.TextField("参数名", parameterName);
        startIndex = EditorGUILayout.IntField("起始参数值", startIndex);
        enableAudioFeature = EditorGUILayout.Toggle("启用音频绑定", enableAudioFeature);
        menuNamingStyle = (NamingStyle)EditorGUILayout.EnumPopup("菜单按钮命名方式", menuNamingStyle);
        EditorGUILayout.EndVertical();
        EditorGUILayout.Space();

        // 过渡设置
        EditorGUILayout.BeginVertical("box", GUILayout.ExpandWidth(true));
        EditorGUILayout.LabelField("过渡设置", EditorStyles.boldLabel);
        transitionHasExitTime = EditorGUILayout.Toggle("启用退出时间", transitionHasExitTime);
        transitionExitTime = Mathf.Clamp01(EditorGUILayout.FloatField("退出时间 (0-1)", transitionExitTime));
        transitionHasFixedDuration = EditorGUILayout.Toggle("固定时长", transitionHasFixedDuration);
        transitionDuration = Mathf.Max(0f, EditorGUILayout.FloatField("过渡时长(秒)", transitionDuration));
        EditorGUILayout.EndVertical();
        EditorGUILayout.Space();

        // 操作按钮
        GUI.enabled = animationAudioPairs.Any(p => p.AnimationClip != null);
        if (GUILayout.Button("1. 创建动画控制器", GUILayout.Height(40), GUILayout.ExpandWidth(true)))
        {
            CreateController();
        }
        GUI.enabled = (lastController != null && lastClipCount > 0 && !string.IsNullOrEmpty(parameterName));
        if (GUILayout.Button("2. 创建MA预制件（带音频开关）", GUILayout.Height(40), GUILayout.ExpandWidth(true)))
        {
            CreateMAPrefabWithSubMenu();
        }
        GUI.enabled = true;
        EditorGUILayout.Space();

        if (GUILayout.Button("定位输出文件夹", GUILayout.Height(30), GUILayout.ExpandWidth(true)))
        {
            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(BaseFolder));
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(BaseFolder);
        }
        EditorGUILayout.Space();

        // 状态日志
        EditorGUILayout.BeginVertical("box", GUILayout.ExpandWidth(true));
        EditorGUILayout.LabelField("状态日志", EditorStyles.boldLabel);
        EditorGUILayout.TextArea(statusMessage, GUILayout.ExpandHeight(true));
        EditorGUILayout.EndVertical();

        EditorGUILayout.EndVertical();
        EditorGUILayout.EndHorizontal();
    }

    // 拆分拖拽区域为动画拖入 + 音频拖入
    private void DrawSplitDragAreas(float parentWidth)
    {
        EditorGUILayout.BeginHorizontal();

        // 动画拖入区域（占50%）
        float dragAreaWidth = (parentWidth - 10) / 2;
        DrawSingleDragArea(dragAreaWidth, "🎬 动画拖入\n（AnimationClip/文件夹）", DragAreaType.Animation);

        GUILayout.Space(10);

        // 音频拖入区域（仅启用音频时显示，占50%）
        if (enableAudioFeature)
        {
            DrawSingleDragArea(dragAreaWidth, "🔊 音频拖入\n（AudioClip/文件夹）", DragAreaType.Audio);
        }
        else
        {
            // 占位符（保持布局对称）
            GUILayout.Box("", GUILayout.Width(dragAreaWidth), GUILayout.Height(70));
        }

        EditorGUILayout.EndHorizontal();
    }

    // 拖拽区域类型枚举
    private enum DragAreaType
    {
        Animation,
        Audio
    }

    // 绘制单个拖拽区域
    private void DrawSingleDragArea(float width, string label, DragAreaType type)
    {
        Rect dragRect = GUILayoutUtility.GetRect(width, 70, GUILayout.Width(width));
        GUI.Box(dragRect, label, EditorStyles.helpBox);
        var evt = Event.current;

        if (!dragRect.Contains(evt.mousePosition)) return;

        switch (evt.type)
        {
            case EventType.DragUpdated:
            case EventType.DragPerform:
                bool hasValid = false;

                switch (type)
                {
                    case DragAreaType.Animation:
                        hasValid = DragAndDrop.objectReferences.Any(o => o is AnimationClip || IsProjectFolder(o));
                        break;
                    case DragAreaType.Audio:
                        hasValid = DragAndDrop.objectReferences.Any(o => o is AudioClip || IsProjectFolder(o));
                        break;
                }

                DragAndDrop.visualMode = hasValid ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;

                if (evt.type == EventType.DragPerform && hasValid)
                {
                    DragAndDrop.AcceptDrag();
                    foreach (var obj in DragAndDrop.objectReferences)
                    {
                        switch (type)
                        {
                            case DragAreaType.Animation:
                                HandleAnimationDrag(obj);
                                break;
                            case DragAreaType.Audio:
                                HandleAudioDrag(obj);
                                break;
                        }
                    }
                    Event.current.Use();
                    Repaint();
                }
                break;
        }
    }

    // 处理动画拖拽（原有逻辑）
    private void HandleAnimationDrag(UnityEngine.Object obj)
    {
        if (obj is AnimationClip clip && clip != null)
        {
            if (!animationAudioPairs.Any(p => p.AnimationClip == clip))
            {
                animationAudioPairs.Add(new AnimationAudioPair(clip));
                statusMessage += $"已添加动画：{clip.name}\n";
            }
        }
        else if (IsProjectFolder(obj))
        {
            string folder = AssetDatabase.GetAssetPath(obj);
            AddClipsFromFolderRecursive(folder, DragAreaType.Animation);
        }
    }

    // 处理音频拖拽：优先填充第一个空动画槽位
    private void HandleAudioDrag(UnityEngine.Object obj)
    {
        if (!enableAudioFeature) return;

        if (obj is AudioClip clip && clip != null)
        {
            // 查找第一个没有音频的动画项
            var emptyAudioPair = animationAudioPairs.FirstOrDefault(p => p.AnimationClip != null && p.AudioClip == null);
            if (emptyAudioPair != null)
            {
                emptyAudioPair.AudioClip = clip;
                statusMessage += $"已为动画 {emptyAudioPair.AnimationClip.name} 匹配音频：{clip.name}\n";
            }
            else
            {
                // 无空槽位时提示
                statusMessage += $"⚠️ 无空动画槽位可填充音频：{clip.name}\n";
            }
        }
        else if (IsProjectFolder(obj))
        {
            string folder = AssetDatabase.GetAssetPath(obj);
            AddClipsFromFolderRecursive(folder, DragAreaType.Audio);
        }
    }

    // 批量导入文件夹内的动画/音频
    private void AddClipsFromFolderRecursive(string folderPath, DragAreaType type)
    {
        string filter = type == DragAreaType.Animation ? "t:AnimationClip" : "t:AudioClip";
        var guids = AssetDatabase.FindAssets(filter, new[] { folderPath });

        foreach (var guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (type == DragAreaType.Animation)
            {
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (clip != null && !animationAudioPairs.Any(p => p.AnimationClip == clip))
                {
                    animationAudioPairs.Add(new AnimationAudioPair(clip));
                    statusMessage += $"从文件夹添加动画：{clip.name}\n";
                }
            }
            else if (type == DragAreaType.Audio)
            {
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (clip != null)
                {
                    // 批量填充音频：按顺序填充空槽位
                    var emptyAudioPair = animationAudioPairs.FirstOrDefault(p => p.AnimationClip != null && p.AudioClip == null);
                    if (emptyAudioPair != null)
                    {
                        emptyAudioPair.AudioClip = clip;
                        statusMessage += $"从文件夹为动画 {emptyAudioPair.AnimationClip.name} 匹配音频：{clip.name}\n";
                    }
                }
            }
        }
    }

    private static bool IsProjectFolder(UnityEngine.Object obj)
    {
        if (obj == null) return false;
        string path = AssetDatabase.GetAssetPath(obj);
        return !string.IsNullOrEmpty(path) && AssetDatabase.IsValidFolder(path);
    }

    private void CreateController()
    {
        try
        {
            string sanitizedName = SanitizeFileName(controllerName);
            string outputFolder = $"{BaseFolder}/{sanitizedName}";
            EnsureFolder(outputFolder);

            var validPairs = animationAudioPairs.Where(p => p.AnimationClip != null).ToList();
            if (validPairs.Count == 0)
            {
                statusMessage = "错误：无有效动画片段！\n";
                return;
            }

            string controllerPath = $"{outputFolder}/{sanitizedName}.controller";
            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);

            AnimatorControllerLayer layer = controller.layers[0];
            AnimatorStateMachine sm = layer.stateMachine;

            sm.states = new ChildAnimatorState[0];
            sm.anyStateTransitions = new AnimatorStateTransition[0];
            sm.entryTransitions = new AnimatorTransition[0];

            if (!controller.parameters.Any(p => p.name == parameterName))
            {
                controller.AddParameter(parameterName, AnimatorControllerParameterType.Int);
            }

            AnimatorState emptyState = sm.AddState("Empty");
            emptyState.writeDefaultValues = true;

            List<AnimatorState> animStates = new List<AnimatorState>();
            for (int i = 0; i < validPairs.Count; i++)
            {
                int paramValue = startIndex + i;
                var clip = validPairs[i].AnimationClip;
                AnimatorState state = sm.AddState(clip.name);
                state.motion = clip;
                state.writeDefaultValues = true;
                animStates.Add(state);

                AnimatorStateTransition transitionTo = emptyState.AddTransition(state);
                transitionTo.AddCondition(AnimatorConditionMode.Equals, paramValue, parameterName);
                transitionTo.hasExitTime = transitionHasExitTime;
                transitionTo.exitTime = transitionExitTime;
                transitionTo.hasFixedDuration = transitionHasFixedDuration;
                transitionTo.duration = transitionDuration;
                transitionTo.canTransitionToSelf = false;

                AnimatorStateTransition transitionBack = state.AddTransition(emptyState);
                transitionBack.AddCondition(AnimatorConditionMode.NotEqual, paramValue, parameterName);
                transitionBack.hasExitTime = transitionHasExitTime;
                transitionBack.exitTime = transitionExitTime;
                transitionBack.hasFixedDuration = transitionHasFixedDuration;
                transitionBack.duration = transitionDuration;
                transitionBack.canTransitionToSelf = false;
            }

            AssetDatabase.SaveAssets();
            lastController = controller;
            lastClipCount = validPairs.Count;
            lastStartIndex = startIndex;
            lastGeneratedFolderPath = outputFolder;

            statusMessage = $"✅ 成功创建动画控制器：{sanitizedName}\n" +
                           $"包含 {validPairs.Count} 个动画状态\n" +
                           $"参数名：{parameterName} (起始值：{startIndex})\n" +
                           $"过渡配置：退出时间={transitionHasExitTime}（值：{transitionExitTime}），固定时长={transitionHasFixedDuration}（时长：{transitionDuration}秒）";
        }
        catch (Exception e)
        {
            statusMessage = $"❌ 创建控制器失败：{e.Message}\n{e.StackTrace}\n";
        }
    }

    private void CreateMAPrefabWithSubMenu()
    {
        try
        {
            statusMessage = "开始创建MA预制件...\n";

            string sanitizedName = SanitizeFileName(controllerName);
            string outputFolder = $"{BaseFolder}/{sanitizedName}";
            EnsureFolder(outputFolder);

            GameObject rootObj = new GameObject($"MA_{sanitizedName}");

            ModularAvatarMergeAnimator mergeAnim = rootObj.AddComponent<ModularAvatarMergeAnimator>();
            mergeAnim.animator = lastController;
            mergeAnim.layerType = VRCAvatarDescriptor.AnimLayerType.Action;
            mergeAnim.pathMode = MergeAnimatorPathMode.Absolute;
            mergeAnim.layerPriority = 9;

            Dictionary<int, GameObject> audioObjMap = new Dictionary<int, GameObject>();
            GameObject audioRoot = null;

            if (enableAudioFeature)
            {
                audioRoot = new GameObject("AudioObjects");
                audioRoot.transform.SetParent(rootObj.transform);
                audioRoot.SetActive(true);

                var validPairs = animationAudioPairs.Where(p => p.AnimationClip != null).ToList();
                for (int i = 0; i < validPairs.Count; i++)
                {
                    int paramValue = startIndex + i;
                    var pair = validPairs[i];

                    GameObject audioObj = new GameObject(pair.AnimationClip.name + "_Audio");
                    audioObj.transform.SetParent(audioRoot.transform);
                    audioObj.SetActive(false);

                    if (pair.AudioClip != null)
                    {
                        AudioSource audioSource = audioObj.AddComponent<AudioSource>();
                        audioSource.clip = pair.AudioClip;
                        audioSource.playOnAwake = false;
                        audioSource.loop = false;
                        audioSource.spatialBlend = 0f;
                    }

                    audioObjMap.Add(paramValue, audioObj);
                    statusMessage += $"✅ 创建音频对象：{audioObj.name}\n";
                }
            }

            GameObject menuRoot = new GameObject("Menu");
            menuRoot.transform.SetParent(rootObj.transform);

            ModularAvatarMenuItem mainMenu = menuRoot.AddComponent<ModularAvatarMenuItem>();
            mainMenu.Control.type = VRCExpressionsMenu.Control.ControlType.SubMenu;
            mainMenu.Control.name = sanitizedName;
            mainMenu.MenuSource = SubmenuSource.Children;
            menuRoot.AddComponent<ModularAvatarMenuInstaller>();

            ModularAvatarParameters paramComp = menuRoot.AddComponent<ModularAvatarParameters>();
            ParameterConfig paramConfig = new ParameterConfig
            {
                nameOrPrefix = parameterName,
                syncType = ParameterSyncType.Int,
                saved = false,
                defaultValue = startIndex - 1
            };
            paramComp.parameters.Add(paramConfig);

            var validPairsForMenu = animationAudioPairs.Where(p => p.AnimationClip != null).ToList();
            for (int i = 0; i < validPairsForMenu.Count; i++)
            {
                int paramValue = startIndex + i;
                var pair = validPairsForMenu[i];

                string buttonName = menuNamingStyle == NamingStyle.UseAnimationName
                    ? pair.AnimationClip.name
                    : $"Action {paramValue}";

                GameObject toggleObj = new GameObject($"Toggle_{buttonName}");
                toggleObj.transform.SetParent(menuRoot.transform);

                ModularAvatarMenuItem toggleMenu = toggleObj.AddComponent<ModularAvatarMenuItem>();
                toggleMenu.Control.type = VRCExpressionsMenu.Control.ControlType.Toggle;
                toggleMenu.Control.name = buttonName;
                toggleMenu.Control.parameter = new VRCExpressionsMenu.Control.Parameter { name = parameterName };
                toggleMenu.Control.value = paramValue;
                toggleMenu.automaticValue = false;

                if (enableAudioFeature && audioObjMap.ContainsKey(paramValue))
                {
                    GameObject audioObj = audioObjMap[paramValue];
                    ModularAvatarObjectToggle objToggle = toggleObj.AddComponent<ModularAvatarObjectToggle>();

                    if (HasProperty(objToggle, "Objects"))
                    {
                        var objectsProp = objToggle.GetType().GetProperty("Objects", BindingFlags.Public | BindingFlags.Instance);
                        if (objectsProp != null)
                        {
                            var objectsList = objectsProp.GetValue(objToggle) as List<ToggledObject>;
                            if (objectsList == null)
                            {
                                objectsList = new List<ToggledObject>();
                                objectsProp.SetValue(objToggle, objectsList);
                            }

                            ToggledObject toggledObj = new ToggledObject();
                            toggledObj.Object = new AvatarObjectReference();
                            toggledObj.Object.Set(audioObj);
                            toggledObj.Active = true;

                            objectsList.Add(toggledObj);
                            statusMessage += $"✅ 绑定音频到Objects：{audioObj.name} → {toggleObj.name}\n";
                        }
                    }

                    if (HasProperty(objToggle, "mode"))
                    {
                        var modeProp = objToggle.GetType().GetProperty("mode", BindingFlags.Public | BindingFlags.Instance);
                        if (modeProp != null && modeProp.PropertyType.IsEnum)
                        {
                            foreach (var enumVal in Enum.GetValues(modeProp.PropertyType))
                            {
                                if (enumVal.ToString().Equals("Toggle", StringComparison.OrdinalIgnoreCase))
                                {
                                    modeProp.SetValue(objToggle, enumVal);
                                    break;
                                }
                            }
                        }
                    }

                    EditorUtility.SetDirty(objToggle);
                }

                EditorUtility.SetDirty(toggleMenu);
            }

            string prefabPath = $"{outputFolder}/{sanitizedName}_MA.prefab";
            if (PrefabUtility.SaveAsPrefabAsset(rootObj, prefabPath))
            {
                statusMessage += $"✅ 成功创建MA预制件：{sanitizedName}_MA.prefab\n输出路径：{outputFolder}\n";
            }
            else
            {
                statusMessage += $"❌ 保存MA预制件失败！\n";
            }

            GameObject.DestroyImmediate(rootObj);
            AssetDatabase.Refresh();
            AssetDatabase.SaveAssets();

            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(prefabPath));
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(prefabPath);
        }
        catch (Exception e)
        {
            statusMessage = $"❌ 创建MA预制件失败：{e.Message}\n{e.StackTrace}\n";
        }
    }

    private bool HasProperty(object obj, string propName)
    {
        return obj != null && obj.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.Instance) != null;
    }

    private static void EnsureFolder(string fullPath)
    {
        if (!AssetDatabase.IsValidFolder(fullPath))
        {
            string[] pathParts = fullPath.Split('/');
            string currentPath = "";
            foreach (string part in pathParts)
            {
                if (string.IsNullOrEmpty(part)) continue;
                string newPath = currentPath == "" ? part : $"{currentPath}/{part}";
                if (!AssetDatabase.IsValidFolder(newPath))
                {
                    AssetDatabase.CreateFolder(currentPath == "" ? "Assets" : currentPath, part);
                }
                currentPath = newPath;
            }
        }
    }

    private static string SanitizeFileName(string name)
    {
        if (string.IsNullOrEmpty(name)) return "MA_Animator";
        foreach (char c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c.ToString(), "_");
        }
        return name;
    }
}
#endif
