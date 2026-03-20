#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
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

    // 统一过渡选项（对齐1.0版本命名）
    private bool transitionsUseExitTime = false;
    private float transitionsExitTime = 0f;
    private bool transitionsFixedDuration = true;
    private float transitionsDuration = 0f;

    // 音频功能（保留2.0新增功能）
    private bool enableAudioFeature = false;
    private List<AnimationAudioPair> animationAudioPairs = new List<AnimationAudioPair>();
    private Vector2 scroll;
    private string statusMessage = "";
    private AnimatorController lastController = null;
    private int lastClipCount = 0;
    private int lastStartIndex = 1;
    private string lastGeneratedFolderPath = "";

    // 菜单按钮命名方式（保留2.0新增）
    private enum NamingStyle
    {
        UseAnimationName,
        UseUniformNaming
    }
    private NamingStyle menuNamingStyle = NamingStyle.UseAnimationName;

    // 动画-音频配对结构（保留2.0新增）
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

    // 修正工具入口（对齐1.0版本名称）
    [MenuItem("LGC/LGC Action 层动画器批量构建（MA 版）")]
    public static void OpenWindow()
    {
        var win = GetWindow<ActionAnimatorBuilderMA>("LGC Action 层动画器批量构建（MA 版）");
        win.minSize = new Vector2(950, 600);
    }

    private void OnEnable() => EnsureFolder(BaseFolder);

    private void OnGUI()
    {
        EditorGUILayout.LabelField("LGC Action 层动画器批量构建（MA 版）", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        // ========== 核心修复：自适应宽度计算（解决右侧面板截断问题） ==========
        // 1. 获取窗口可用宽度（减去 Editor 默认边距 20px，避免内容溢出）
        float windowAvailableWidth = position.width - 20;
        // 2. 定义左右面板间距
        float horizontalSpacing = 10;
        // 3. 左侧宽度：优先 2/3 比例，最小 450px，最大不超过可用宽度的 70%（避免挤压右侧）
        float leftWidth = Mathf.Clamp(windowAvailableWidth * 0.666f, 450, windowAvailableWidth * 0.7f);
        // 4. 右侧宽度：用剩余宽度 - 间距，最小 250px（保证核心操作区不被挤没）
        float rightWidth = Mathf.Max(250, windowAvailableWidth - leftWidth - horizontalSpacing);

        // 左右布局 - 保留原有2:1比例，优化自适应
        EditorGUILayout.BeginHorizontal();

        // 左侧：动画/音频列表 (自适应宽度，无截断)
        // 修正：移除错误的 FlexibleWidth，用 Width + MinWidth + ExpandWidth 实现自适应
        EditorGUILayout.BeginVertical(
            GUILayout.MinWidth(leftWidth),    // 最小宽度保障（不小于450px）
            GUILayout.Width(leftWidth),       // 基础宽度
            GUILayout.ExpandWidth(true),      // 允许拉伸填充空间
            GUILayout.ExpandHeight(true)      // 高度铺满窗口
        );
        EditorGUILayout.LabelField("一站式完成动画控制器创建、MA 预制件生成及音频绑定", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("替代重复的手动配置流程；", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("在确定一切之前，不要关闭面板", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("做完了建议把生成的控制器和预制件移动到别的地方，防止覆盖或误操作", EditorStyles.boldLabel);

        // 拆分拖拽区域为动画拖入 + 音频拖入（仅启用音频时显示）（保留2.0）
        DrawSplitDragAreas(leftWidth);
        EditorGUILayout.Space();

        // 清空/排序按钮（适配2.0的animationAudioPairs）
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

        // 列表区域（适配2.0的音频功能）
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

        // 显式添加间距，避免左右面板挤在一起
        GUILayout.Space(horizontalSpacing);

        // 右侧：操作面板 (自适应宽度，完整显示无截断)
        // 修正：移除错误的 FlexibleWidth，用 Width + MinWidth + ExpandWidth 实现自适应
        EditorGUILayout.BeginVertical(
            GUILayout.MinWidth(rightWidth),   // 最小宽度保障（不小于250px）
            GUILayout.Width(rightWidth),      // 基础宽度
            GUILayout.ExpandWidth(true),      // 允许拉伸填充空间
            GUILayout.ExpandHeight(true)     // 高度铺满窗口
        );

        // 基础设置（对齐1.0版本）
        EditorGUILayout.BeginVertical("box", GUILayout.ExpandWidth(true));
        EditorGUILayout.LabelField("基础设置", EditorStyles.boldLabel);
        controllerName = EditorGUILayout.TextField("文件名", controllerName);
        parameterName = EditorGUILayout.TextField("参数名（int）", parameterName);
        startIndex = EditorGUILayout.IntField("起始编号", startIndex);
        enableAudioFeature = EditorGUILayout.Toggle("启用音频绑定", enableAudioFeature);
        menuNamingStyle = (NamingStyle)EditorGUILayout.EnumPopup("菜单按钮命名方式", menuNamingStyle);
        EditorGUILayout.EndVertical();
        EditorGUILayout.Space();

        // 过渡设置（完全对齐1.0版本）
        EditorGUILayout.BeginVertical("box", GUILayout.ExpandWidth(true));
        EditorGUILayout.LabelField("过渡选项", EditorStyles.boldLabel);
        transitionsUseExitTime = EditorGUILayout.Toggle("启用退出时间", transitionsUseExitTime);
        transitionsExitTime = Mathf.Clamp01(EditorGUILayout.FloatField("退出时间 (0..1)", transitionsExitTime));
        transitionsFixedDuration = EditorGUILayout.Toggle("固定时长", transitionsFixedDuration);
        transitionsDuration = Mathf.Max(0f, EditorGUILayout.FloatField("过渡时长 (秒)", transitionsDuration));
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

    // 拆分拖拽区域为动画拖入 + 音频拖入（保留2.0）
    private void DrawSplitDragAreas(float parentWidth)
    {
        EditorGUILayout.BeginHorizontal();

        // 动画拖入区域（占50%）
        float dragAreaWidth = (parentWidth - 10) / 2;
        DrawSingleDragArea(dragAreaWidth, "动画拖入\n（AnimationClip/文件夹）", DragAreaType.Animation);

        GUILayout.Space(10);

        // 音频拖入区域（仅启用音频时显示，占50%）
        if (enableAudioFeature)
        {
            DrawSingleDragArea(dragAreaWidth, "音频拖入\n（AudioClip/文件夹）", DragAreaType.Audio);
        }
        else
        {
            // 占位符（保持布局对称）
            GUILayout.Box("", GUILayout.Width(dragAreaWidth), GUILayout.Height(70));
        }

        EditorGUILayout.EndHorizontal();
    }

    // 拖拽区域类型枚举（保留2.0）
    private enum DragAreaType
    {
        Animation,
        Audio
    }

    // 绘制单个拖拽区域（保留2.0）
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

    // 处理动画拖拽（保留2.0，适配1.0逻辑）
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

    // 处理音频拖拽（保留2.0）
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
                statusMessage += $"无空动画槽位可填充音频：{clip.name}\n";
            }
        }
        else if (IsProjectFolder(obj))
        {
            string folder = AssetDatabase.GetAssetPath(obj);
            AddClipsFromFolderRecursive(folder, DragAreaType.Audio);
        }
    }

    // 批量导入文件夹内的动画/音频（保留2.0）
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

    // 判断是否为项目文件夹（对齐1.0）
    private static bool IsProjectFolder(UnityEngine.Object obj)
    {
        if (obj == null) return false;
        string path = AssetDatabase.GetAssetPath(obj);
        return !string.IsNullOrEmpty(path) && AssetDatabase.IsValidFolder(path);
    }

    // 创建控制器（完全对齐1.0版本逻辑，适配2.0的音频配对列表）
    private void CreateController()
    {
        try
        {
            string sanitizedControllerName = SanitizeFileName(controllerName);
            string controllerFolder = $"{BaseFolder}/{sanitizedControllerName}";
            EnsureFolder(controllerFolder);

            // 从2.0的音频配对列表中提取有效动画片段（对齐1.0的clips）
            var validClips = animationAudioPairs.Where(p => p.AnimationClip != null).Select(p => p.AnimationClip).ToList();
            if (validClips.Count == 0)
            {
                statusMessage = "创建失败：无有效动画。";
                return;
            }

            string basePath = $"{controllerFolder}/{sanitizedControllerName}.controller";
            string targetPath = AssetDatabase.GenerateUniqueAssetPath(basePath);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(targetPath);

            var layer = controller.layers[0];
            var sm = layer.stateMachine;
            sm.anyStatePosition = new Vector3(40, 220, 0);
            sm.entryPosition = new Vector3(40, 100, 0);
            sm.exitPosition = new Vector3(1200, 100, 0);

            if (string.IsNullOrEmpty(parameterName)) parameterName = "pose";
            var exist = controller.parameters.FirstOrDefault(p => p.name == parameterName);
            if (exist == null || exist.type != AnimatorControllerParameterType.Int)
                controller.AddParameter(parameterName, AnimatorControllerParameterType.Int);

            // 1.0版本核心状态机结构
            var empty0 = sm.AddState("Empty_0_Default", new Vector3(160, 100, 0));
            sm.defaultState = empty0;
            var empty1 = sm.AddState("Empty_1_Gate", new Vector3(360, 100, 0));
            var t01 = empty0.AddTransition(empty1);
            ConfigureTransition(t01);
            t01.AddCondition(AnimatorConditionMode.NotEqual, 0, parameterName);

            var states = new List<AnimatorState>();
            CreateGridStates(validClips, sm, states);
            for (int i = 0; i < states.Count; i++)
            {
                int value = startIndex + i;
                var t = empty1.AddTransition(states[i]);
                ConfigureTransition(t);
                t.AddCondition(AnimatorConditionMode.Equals, value, parameterName);
            }

            var empty2 = sm.AddState("Empty_2_BeforeExit", new Vector3(980, 100, 0));
            for (int i = 0; i < states.Count; i++)
            {
                int value = startIndex + i;
                var t = states[i].AddTransition(empty2);
                ConfigureTransition(t);
                t.AddCondition(AnimatorConditionMode.NotEqual, value, parameterName);
            }

            var exitT = empty2.AddExitTransition();
            ConfigureTransition(exitT);
            // 修复BUG - 为Exit过渡添加参数不等于0的条件
            exitT.AddCondition(AnimatorConditionMode.NotEqual, 0, parameterName);

            // 可选：在门控/退出前状态上附加 VRC 行为（1.0版本核心逻辑）
            string info1a, info1b, info2a, info2b;
            TryAddVRCTrackingControl(empty1, allSetToAnimation: true, out info1a);
            TryAddVRCPlayableLayerControl(empty1, playableName: "Action", layerIndex: 0,
                goalWeight: 1f, blendDuration: 0.5f, out info1b);
            TryAddVRCTrackingControl(empty2, allSetToAnimation: false, out info2a);
            TryAddVRCPlayableLayerControl(empty2, playableName: "Action", layerIndex: 0,
                goalWeight: 0f, blendDuration: 0.25f, out info2b);

            AssetDatabase.SaveAssets();
            lastController = controller;
            lastClipCount = states.Count;
            lastStartIndex = startIndex;
            lastGeneratedFolderPath = controllerFolder;

            statusMessage =
                $"✅ Animator 创建成功：{Path.GetFileName(targetPath)}\n" +
                $"包含 {states.Count} 个动画状态\n" +
                $"参数名：{parameterName} (起始值：{startIndex})\n" +
                $"过渡配置：退出时间={transitionsUseExitTime}（值：{transitionsExitTime}），固定时长={transitionsFixedDuration}（时长：{transitionsDuration}秒）\n" +
                $"空1：{info1a} \n {info1b}\n空2：{info2a} \n {info2b}";

            // 自动定位到生成的文件夹
            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(controllerFolder));
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(controllerFolder);
        }
        catch (Exception e)
        {
            statusMessage = $"❌ 创建控制器失败：{e.Message}\n{e.StackTrace}\n";
        }
    }

    // 网格布局创建动画状态（1.0版本核心方法）
    private void CreateGridStates(List<AnimationClip> validClips, AnimatorStateMachine sm, List<AnimatorState> states)
    {
        int n = validClips.Count;
        int cols = Mathf.Clamp(Mathf.CeilToInt(Mathf.Sqrt(n)), 1, 8);
        float startX = 540f, startY = 80f, dx = 160f, dy = 120f;
        for (int i = 0; i < n; i++)
        {
            int r = i / cols, c = i % cols;
            var pos = new Vector3(startX + c * dx, startY + r * dy, 0);
            var clip = validClips[i];
            string uniqueName = MakeUniqueStateName(sm, clip.name);
            var s = sm.AddState(uniqueName, pos);
            s.motion = clip;
            s.writeDefaultValues = true;
            states.Add(s);
        }
    }

    // 配置过渡参数（1.0版本核心方法）
    private void ConfigureTransition(AnimatorStateTransition t)
    {
        t.hasExitTime = transitionsUseExitTime;
        t.exitTime = transitionsExitTime;
        t.hasFixedDuration = transitionsFixedDuration;
        t.duration = transitionsDuration;
        t.canTransitionToSelf = false;
        t.interruptionSource = TransitionInterruptionSource.None;
        t.orderedInterruption = false;
    }

    // ========== 核心修复：创建MA预制件方法 ==========
    private void CreateMAPrefabWithSubMenu()
    {
        try
        {
            if (lastController == null || lastClipCount <= 0)
            {
                statusMessage = "请先创建动画控制器。";
                return;
            }

            statusMessage = "开始创建MA预制件...\n";

            string sanitizedControllerName = SanitizeFileName(controllerName);
            string prefabFolder = $"{BaseFolder}/{sanitizedControllerName}";
            EnsureFolder(prefabFolder);

            // 根对象（对齐1.0命名）
            var rootGO = new GameObject("动作包MA");

            // 合并 Animator 到 Action 层（1.0版本逻辑）
            var merge = rootGO.AddComponent<ModularAvatarMergeAnimator>();
            merge.animator = lastController;
            merge.layerType = VRCAvatarDescriptor.AnimLayerType.Action;

            // 设置为绝对路径模式（1.0版本逻辑）
            var pathModeField = typeof(ModularAvatarMergeAnimator).GetField("pathMode", BindingFlags.Public | BindingFlags.Instance);
            if (pathModeField != null && pathModeField.FieldType.IsEnum)
            {
                try
                {
                    var pathModeEnum = Enum.Parse(pathModeField.FieldType, "Absolute", true);
                    pathModeField.SetValue(merge, pathModeEnum);
                }
                catch { }
            }

            // 设置动画层合并优先级为9（1.0版本逻辑）
            var priorityField = typeof(ModularAvatarMergeAnimator).GetField("layerPriority", BindingFlags.Public | BindingFlags.Instance);
            if (priorityField != null)
            {
                priorityField.SetValue(merge, 9);
            }

            // 音频对象管理（保留2.0新增功能 + 修复初始化逻辑）
            Dictionary<int, GameObject> audioObjMap = new Dictionary<int, GameObject>();
            GameObject audioRoot = null;

            if (enableAudioFeature)
            {
                audioRoot = new GameObject("AudioObjects");
                audioRoot.transform.SetParent(rootGO.transform);
                audioRoot.SetActive(true);

                var validPairs = animationAudioPairs.Where(p => p.AnimationClip != null).ToList();
                for (int i = 0; i < validPairs.Count; i++)
                {
                    int paramValue = startIndex + i;
                    var pair = validPairs[i];

                    GameObject audioObj = new GameObject(pair.AnimationClip.name + "_Audio");
                    audioObj.transform.SetParent(audioRoot.transform);
                    audioObj.SetActive(false); // 默认禁用

                    if (pair.AudioClip != null)
                    {
                        // 修复：优化AudioSource初始化，确保配置完整
                        AudioSource audioSource = audioObj.AddComponent<AudioSource>();
                        audioSource.clip = pair.AudioClip;
                        audioSource.playOnAwake = false; // 由MA控制播放，禁用自动播放
                        audioSource.loop = false;
                        audioSource.spatialBlend = 0f;
                        audioSource.volume = 1f;
                        audioSource.enabled = true;
                        EditorUtility.SetDirty(audioSource);
                    }

                    audioObjMap.Add(paramValue, audioObj);
                    statusMessage += $"✅ 创建音频对象：{audioObj.name}\n";
                }
            }

            // 父级子菜单（SubMenu / Children）并安装到 Avatar（1.0版本核心逻辑）
            var menuRoot = new GameObject("动作菜单");
            menuRoot.transform.SetParent(rootGO.transform, false);

            var parentItem = menuRoot.AddComponent<ModularAvatarMenuItem>();
            parentItem.automaticValue = true;
            parentItem.Control.type = VRCExpressionsMenu.Control.ControlType.SubMenu;   // 类型：子菜单
            parentItem.MenuSource = SubmenuSource.Children;                           // 子菜单来源：Children
            menuRoot.AddComponent<ModularAvatarMenuInstaller>();                        // 绑定安装器

            // Parameters：声明 Int 参数（Saved× / Synced√ 由 MA 处理）（1.0版本逻辑）
            var mp = menuRoot.AddComponent<ModularAvatarParameters>();
            var pc = new ParameterConfig
            {
                nameOrPrefix = string.IsNullOrEmpty(parameterName) ? "pose" : parameterName,
                syncType = ParameterSyncType.Int,
                saved = false
            };
            mp.parameters.Add(pc);

            // 子项：为每个动画创建 Toggle（核心修复区域）
            var validPairsForMenu = animationAudioPairs.Where(p => p.AnimationClip != null).ToList();
            for (int i = 0; i < validPairsForMenu.Count; i++)
            {
                int value = startIndex + i;
                var pair = validPairsForMenu[i];

                // 菜单按钮命名（保留2.0选项）
                string buttonName = menuNamingStyle == NamingStyle.UseAnimationName
                    ? pair.AnimationClip.name
                    : $"动作_{value}";

                var itemGO = new GameObject(buttonName);
                itemGO.transform.SetParent(menuRoot.transform, false); // 成为父级的子对象，Children 将收集

                var mi = itemGO.AddComponent<ModularAvatarMenuItem>();
                mi.Control.type = VRCExpressionsMenu.Control.ControlType.Toggle;
                mi.Control.name = buttonName;
                mi.Control.parameter = new VRCExpressionsMenu.Control.Parameter { name = pc.nameOrPrefix };

                // 设置为自动模式（1.0版本逻辑）
                var autoProp = typeof(ModularAvatarMenuItem).GetProperty("automaticValue", BindingFlags.Public | BindingFlags.Instance);
                if (autoProp != null && autoProp.CanWrite)
                {
                    try { autoProp.SetValue(mi, true, null); }
                    catch { mi.Control.value = value; }
                }
                else
                {
                    mi.Control.value = value; // 显式值递增（OFF=0；ON=1..N）
                }

                // 音频绑定逻辑（核心修复）
                if (enableAudioFeature && audioObjMap.ContainsKey(value))
                {
                    GameObject audioObj = audioObjMap[value];
                    ModularAvatarObjectToggle objToggle = itemGO.AddComponent<ModularAvatarObjectToggle>();

                    // ========== 修复1：手动计算相对路径 + 反射设置内部字段 ==========
                    AvatarObjectReference audioRef = new AvatarObjectReference();
                    // 1. 先调用公开的 Set 方法初始化
                    audioRef.Set(audioObj);
                    // 2. 手动计算并修正 referencePath（MA 识别的核心）
                    string relativePath = GetRelativePath(rootGO.transform, audioObj.transform);
                    SetPrivateField(audioRef, "referencePath", relativePath);
                    // 3. 反射设置 internal 的 targetObject 字段（绕过访问权限）
                    SetPrivateField(audioRef, "targetObject", audioObj);

                    // ========== 修复2：配置 ObjectToggle 并强制设置 ResetOnExit ==========
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
                            toggledObj.Object = audioRef;
                            toggledObj.Active = true;

                            // 强制设置 ResetOnExit（MA 必须）
                            if (HasProperty(toggledObj, "ResetOnExit"))
                            {
                                var resetProp = toggledObj.GetType().GetProperty("ResetOnExit", BindingFlags.Public | BindingFlags.Instance);
                                if (resetProp != null) resetProp.SetValue(toggledObj, true);
                            }

                            objectsList.Add(toggledObj);
                            statusMessage += $"✅ 手动绑定音频路径：{relativePath} → {itemGO.name}\n";
                        }
                    }

                    // ========== 修复3：强制触发 MA 组件的 OnValidate（手动拖入时Unity自动触发） ==========
                    MethodInfo onValidateMethod = objToggle.GetType().GetMethod("OnValidate", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (onValidateMethod != null)
                    {
                        try
                        {
                            onValidateMethod.Invoke(objToggle, null);
                            statusMessage += $"✅ 触发 ObjectToggle.OnValidate 刷新路径\n";
                        }
                        catch (Exception ex)
                        {
                            statusMessage += $"⚠️ 触发 OnValidate 失败：{ex.Message}\n";
                        }
                    }

                    // ========== 修复4：兼容 MA 不同版本的 mode 配置 ==========
                    if (HasProperty(objToggle, "mode"))
                    {
                        var modeProp = objToggle.GetType().GetProperty("mode", BindingFlags.Public | BindingFlags.Instance);
                        if (modeProp != null && modeProp.PropertyType.IsEnum)
                        {
                            object targetMode = null;
                            foreach (var enumVal in Enum.GetValues(modeProp.PropertyType))
                            {
                                string enumName = enumVal.ToString().ToLower();
                                if (enumName == "toggle" || enumName == "enabledisable")
                                {
                                    targetMode = enumVal;
                                    break;
                                }
                            }
                            if (targetMode != null)
                            {
                                modeProp.SetValue(objToggle, targetMode);
                            }
                        }
                    }

                    // ========== 修复5：添加音频播放逻辑 ==========
                    Type audioPlayType = FindTypeAnywhere("nadena.dev.modular_avatar.core.ModularAvatarAudioPlay");
                    if (audioPlayType != null && typeof(MonoBehaviour).IsAssignableFrom(audioPlayType))
                    {
                        var audioPlay = itemGO.AddComponent(audioPlayType) as MonoBehaviour;
                        if (audioPlay != null && pair.AudioClip != null)
                        {
                            var clipProp = audioPlayType.GetProperty("AudioClip", BindingFlags.Public | BindingFlags.Instance);
                            if (clipProp != null && clipProp.CanWrite)
                            {
                                clipProp.SetValue(audioPlay, pair.AudioClip);
                            }
                            var playModeProp = audioPlayType.GetProperty("PlayMode", BindingFlags.Public | BindingFlags.Instance);
                            if (playModeProp != null && playModeProp.PropertyType.IsEnum)
                            {
                                foreach (var enumVal in Enum.GetValues(playModeProp.PropertyType))
                                {
                                    if (enumVal.ToString().ToLower() == "playonenter")
                                    {
                                        playModeProp.SetValue(audioPlay, enumVal);
                                        break;
                                    }
                                }
                            }
                            var stopModeProp = audioPlayType.GetProperty("StopMode", BindingFlags.Public | BindingFlags.Instance);
                            if (stopModeProp != null && stopModeProp.PropertyType.IsEnum)
                            {
                                foreach (var enumVal in Enum.GetValues(stopModeProp.PropertyType))
                                {
                                    if (enumVal.ToString().ToLower() == "stoponexit")
                                    {
                                        stopModeProp.SetValue(audioPlay, enumVal);
                                        break;
                                    }
                                }
                            }
                            EditorUtility.SetDirty(audioPlay);
                        }
                    }
                    else
                    {
                        // 降级方案：启用 AudioSource PlayOnAwake
                        AudioSource audioSource = audioObj.GetComponent<AudioSource>();
                        if (audioSource != null)
                        {
                            audioSource.playOnAwake = true;
                            EditorUtility.SetDirty(audioSource);
                        }
                    }

                    // ========== 修复6：强制序列化 ==========
                    EditorUtility.SetDirty(objToggle);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(objToggle);
                }

                EditorUtility.SetDirty(mi);
                EditorUtility.SetDirty(itemGO);
            }

            // ========== 修复7：保存前强制刷新所有序列化状态 ==========
            // 强制标记根对象及所有子对象为 Dirty
            EditorUtility.SetDirty(rootGO);
            foreach (Transform child in rootGO.transform)
            {
                EditorUtility.SetDirty(child.gameObject);
                foreach (Transform grandChild in child)
                {
                    EditorUtility.SetDirty(grandChild.gameObject);
                }
            }

            // 刷新 MA 层级缓存
            Type runtimeUtilType = FindTypeAnywhere("nadena.dev.modular_avatar.core.RuntimeUtil");
            if (runtimeUtilType != null)
            {
                MethodInfo invalidateMethod = runtimeUtilType.GetMethod("InvalidateAll", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (invalidateMethod != null)
                {
                    invalidateMethod.Invoke(null, null);
                    statusMessage += $"✅ 刷新 MA 层级缓存\n";
                }
            }

            // 保存为 Prefab（1.0版本逻辑）
            string prefabPath = AssetDatabase.GenerateUniqueAssetPath($"{prefabFolder}/{sanitizedControllerName}_MA.prefab");
            PrefabUtility.SaveAsPrefabAsset(rootGO, prefabPath, out bool prefabSuccess);
            UnityEngine.Object.DestroyImmediate(rootGO);

            statusMessage += prefabSuccess
                ? $"✅ MA 预制件创建成功：{Path.GetFileName(prefabPath)}（父级 SubMenu/Children，子项 {lastClipCount} 个 Toggle）"
                : "❌ 创建 MA 预制件失败。";

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkAllScenesDirty();

            // 更新最后生成的文件夹路径
            lastGeneratedFolderPath = prefabFolder;

            // 自动定位到生成的文件夹
            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(prefabFolder));
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(prefabFolder);
        }
        catch (Exception e)
        {
            statusMessage = $"❌ 创建MA预制件失败：{e.Message}\n{e.StackTrace}\n";
        }
    }

    // ========== 新增工具方法1：手动计算相对路径 ==========
    private string GetRelativePath(Transform root, Transform target)
    {
        if (root == target) return "";
        if (target.parent == null) return target.name;

        List<string> pathParts = new List<string>();
        Transform current = target;

        // 从目标对象向上遍历到根对象，收集路径片段
        while (current != root && current != null)
        {
            pathParts.Add(current.name);
            current = current.parent;
        }

        // 反转路径（根→目标）
        pathParts.Reverse();
        return string.Join("/", pathParts);
    }

    // ========== 新增工具方法2：反射设置私有/内部字段（核心修复访问权限问题） ==========
    private void SetPrivateField(object obj, string fieldName, object value)
    {
        if (obj == null) return;

        // 查找字段（包括私有、内部、受保护）
        FieldInfo field = obj.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (field != null)
        {
            field.SetValue(obj, value);
        }
        else
        {
            statusMessage += $"⚠️ 未找到字段 {fieldName}，无法设置值\n";
        }
    }

    // VRChat Behaviours（1.0版本核心方法，保持不变）
    private static bool TryAddVRCTrackingControl(AnimatorState state, bool allSetToAnimation, out string info)
    {
        info = "";
        Type vrcType = FindTypeAnywhere("VRC.SDK3.Avatars.Components.VRCAnimatorTrackingControl")
            ?? FindTypeAnywhere("VRC.SDKBase.VRC_AnimatorTrackingControl");
        if (vrcType == null || !typeof(StateMachineBehaviour).IsAssignableFrom(vrcType))
        {
            info = "TrackingControl 类型缺失";
            return false;
        }

        var behaviour = state.AddStateMachineBehaviour(vrcType);
        if (behaviour == null) { info = "添加失败"; return false; }

        int desiredIndex = allSetToAnimation ? 2 : 1; // 0 NoChange / 1 Tracking / 2 Animation
        string msg = allSetToAnimation ? "All=Animation" : "All=Tracking";

        var temp = ScriptableObject.CreateInstance(vrcType);
        try
        {
            var so = new UnityEditor.SerializedObject(temp);
            var it = so.GetIterator();
            bool enterChildren = true;
            int enumCount = 0;
            while (it.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (it.propertyType == UnityEditor.SerializedPropertyType.Enum)
                {
                    it.enumValueIndex = desiredIndex;
                    enumCount++;
                }
                else if (it.propertyType == UnityEditor.SerializedPropertyType.String &&
                         it.name.Equals("debugString", StringComparison.OrdinalIgnoreCase))
                {
                    it.stringValue = msg;
                }
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            UnityEditor.EditorUtility.CopySerialized(temp, behaviour);
            UnityEditor.EditorUtility.SetDirty(behaviour);
            info = $"TrackingControl 设置 {enumCount} 项（{msg}）";
            return enumCount > 0;
        }
        catch (Exception e)
        {
            info = $"TrackingControl 错误：{e.Message}";
            return false;
        }
        finally
        {
            if (temp != null) UnityEngine.Object.DestroyImmediate(temp);
        }
    }

    // VRChat Behaviours（1.0版本核心方法，保持不变）
    private static bool TryAddVRCPlayableLayerControl(
        AnimatorState state, string playableName, int layerIndex, float goalWeight, float blendDuration, out string info)
    {
        info = "";
        Type plcType = FindTypeAnywhere("VRC.SDK3.Avatars.Components.VRCPlayableLayerControl")
            ?? FindTypeAnywhere("VRC.SDKBase.VRC_PlayableLayerControl");
        if (plcType == null || !typeof(StateMachineBehaviour).IsAssignableFrom(plcType))
        {
            info = "PlayableLayerControl 类型缺失";
            return false;
        }

        var behaviour = state.AddStateMachineBehaviour(plcType);
        if (behaviour == null) { info = "添加失败"; return false; }

        var temp = ScriptableObject.CreateInstance(plcType);
        int fieldsSet = 0;
        try
        {
            object playableEnumVal = null;
            var nestedEnums = plcType.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
                .Where(t => t.IsEnum).ToArray();
            foreach (var ne in nestedEnums)
            {
                var names = Enum.GetNames(ne);
                var target = names.FirstOrDefault(n => string.Equals(n, playableName, StringComparison.OrdinalIgnoreCase));
                if (target != null)
                {
                    playableEnumVal = Enum.Parse(ne, target);
                    var f = plcType.GetFields(BindingFlags.Public | BindingFlags.Instance)
                        .FirstOrDefault(fi => fi.FieldType == ne && fi.Name.ToLowerInvariant().Contains("playable"));
                    if (f != null) { f.SetValue(temp, playableEnumVal); fieldsSet++; break; }
                    var p = plcType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                        .FirstOrDefault(pi => pi.PropertyType == ne && pi.Name.ToLowerInvariant().Contains("playable") && pi.CanWrite);
                    if (p != null) { p.SetValue(temp, playableEnumVal, null); fieldsSet++; break; }
                }
            }

            var so = new UnityEditor.SerializedObject(temp);
            var spLayer = FindIntProperty(so, "layer");
            if (spLayer != null) { spLayer.intValue = layerIndex; fieldsSet++; }
            var spGoal = FindFloatProperty(so, "goalWeight", "goal", "weight");
            if (spGoal != null) { spGoal.floatValue = goalWeight; fieldsSet++; }
            var spBlend = FindFloatProperty(so, "blendDuration", "blend", "duration");
            if (spBlend != null) { spBlend.floatValue = blendDuration; fieldsSet++; }
            so.ApplyModifiedPropertiesWithoutUndo();

            UnityEditor.EditorUtility.CopySerialized(temp, behaviour);
            UnityEditor.EditorUtility.SetDirty(behaviour);
            info = $"Playable(Action) Layer={layerIndex}, Goal={goalWeight}, Blend={blendDuration}（写入 {fieldsSet} 项）";
            return fieldsSet > 0;
        }
        catch (Exception e)
        {
            info = $"PlayableLayerControl 错误：{e.Message}";
            return false;
        }
        finally
        {
            if (temp != null) UnityEngine.Object.DestroyImmediate(temp);
        }
    }

    // 底层序列化辅助（1.0版本核心方法，保持不变）
    private static SerializedProperty FindFloatProperty(SerializedObject so, params string[] nameHints)
    {
        var it = so.GetIterator();
        bool enter = true;
        while (it.NextVisible(enter))
        {
            enter = false;
            if (it.propertyType == SerializedPropertyType.Float)
            {
                string n = it.name.ToLowerInvariant();
                if (nameHints.Any(h => n.Contains(h.ToLowerInvariant())))
                    return it.Copy();
            }
        }
        return null;
    }

    // 底层序列化辅助（1.0版本核心方法，保持不变）
    private static SerializedProperty FindIntProperty(SerializedObject so, params string[] nameHints)
    {
        var it = so.GetIterator();
        bool enter = true;
        while (it.NextVisible(enter))
        {
            enter = false;
            if (it.propertyType == SerializedPropertyType.Integer)
            {
                string n = it.name.ToLowerInvariant();
                if (nameHints.Any(h => n.Contains(h.ToLowerInvariant())))
                    return it.Copy();
            }
        }
        return null;
    }

    // 底层类型查找（1.0版本核心方法，保持不变）
    private static Type FindTypeAnywhere(string fullName)
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (asm.IsDynamic) continue;
            try
            {
                var t = asm.GetType(fullName, throwOnError: false);
                if (t != null) return t;
            }
            catch { }
        }
        return null;
    }

    // 辅助方法（保留2.0）
    private bool HasProperty(object obj, string propName)
    {
        return obj != null && obj.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.Instance) != null;
    }

    // 工具方法（对齐1.0版本）
    private static void EnsureFolder(string fullPath)
    {
        string[] parts = fullPath.Split('/');
        for (int i = 1; i < parts.Length; i++)
        {
            string parent = string.Join("/", parts.Take(i));
            string current = string.Join("/", parts.Take(i + 1));
            if (!AssetDatabase.IsValidFolder(current))
            {
                AssetDatabase.CreateFolder(parent, parts[i]);
            }
        }
    }

    // 工具方法（对齐1.0版本）
    private static string SanitizeFileName(string name)
    {
        if (string.IsNullOrEmpty(name)) return "PoseAnimatorController";
        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c.ToString(), "_");
        return name;
    }

    // 工具方法（1.0版本核心方法）
    private static string MakeUniqueStateName(AnimatorStateMachine sm, string baseName)
    {
        var names = new HashSet<string>(sm.states.Select(s => s.state.name));
        string name = string.IsNullOrEmpty(baseName) ? "State" : baseName;
        if (!names.Contains(name)) return name;
        int i = 1;
        while (true)
        {
            string candidate = $"{name}_{i}";
            if (!names.Contains(candidate)) return candidate;
            i++;
        }
    }
}
#endif
