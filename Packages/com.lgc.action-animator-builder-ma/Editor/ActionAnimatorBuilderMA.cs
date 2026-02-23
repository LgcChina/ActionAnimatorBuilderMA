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

using nadena.dev.modular_avatar.core;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDK3.Avatars.Components;

public class ActionAnimatorBuilderMA : EditorWindow
{
    private const string BaseFolder = "Assets/LGC/Tools/快速批量动画器构建/已创建动画器";
    private string controllerName = "PoseAnimatorController";
    private string parameterName = "pose"; // Int 参数名
    private int startIndex = 1;             // 动画编号起始值

    // 统一过渡选项
    private bool transitionsUseExitTime = true;
    private float transitionsExitTime = 0f;
    private bool transitionsFixedDuration = true;
    private float transitionsDuration = 0f;

    private List<AnimationClip> clips = new List<AnimationClip>();
    private Vector2 scroll;
    private string statusMessage = "";
    private AnimatorController lastController = null;
    private int lastClipCount = 0;
    private int lastStartIndex = 1;
    private string lastGeneratedFolderPath = "";

    [MenuItem("LGC/批量动作动画器构建MA菜单")]
    public static void OpenWindow()
    {
        var win = GetWindow<ActionAnimatorBuilderMA>("批量动作动画器构建MA菜单");
        win.minSize = new Vector2(740, 580);
    }

    private void OnEnable() => EnsureFolder(BaseFolder);

    private void OnGUI()
    {
        EditorGUILayout.LabelField("批量生成 Animator 与 MA 子菜单", EditorStyles.boldLabel);

        // 基本设置
        EditorGUILayout.BeginVertical("box");
        controllerName = EditorGUILayout.TextField("文件名", controllerName);
        parameterName = EditorGUILayout.TextField("参数名（int）", parameterName);
        startIndex = EditorGUILayout.IntField("起始编号", startIndex);
        EditorGUILayout.EndVertical();

        // 过渡设置
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("过渡选项", EditorStyles.boldLabel);
        transitionsUseExitTime = EditorGUILayout.Toggle("启用退出时间", transitionsUseExitTime);
        transitionsExitTime = Mathf.Clamp01(EditorGUILayout.FloatField("退出时间 (0..1)", transitionsExitTime));
        transitionsFixedDuration = EditorGUILayout.Toggle("固定时长", transitionsFixedDuration);
        transitionsDuration = Mathf.Max(0f, EditorGUILayout.FloatField("过渡时长 (秒)", transitionsDuration));
        EditorGUILayout.EndVertical();

        // 拖拽区域与列表
        DrawDragArea();
        EditorGUILayout.LabelField($"动画片段：{clips.Count} 个");
        using (var view = new EditorGUILayout.ScrollViewScope(scroll, GUILayout.Height(180)))
        {
            scroll = view.scrollPosition;
            for (int i = 0; i < clips.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                clips[i] = (AnimationClip)EditorGUILayout.ObjectField(clips[i], typeof(AnimationClip), false);
                if (GUILayout.Button("移除", GUILayout.Width(60)))
                {
                    clips.RemoveAt(i);
                    i--;
                }
                EditorGUILayout.EndHorizontal();
            }
        }
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("清空")) clips.Clear();
        if (GUILayout.Button("按名称排序")) clips = clips.Where(c => c != null).OrderBy(c => c.name).ToList();
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.Space();

        // 1) 创建控制器
        GUI.enabled = clips.Any(c => c != null);
        if (GUILayout.Button("创建控制器", GUILayout.Height(34))) CreateController();
        GUI.enabled = true;

        // 2) 创建 MA 预制件及开关（父级 SubMenu + Children；子项 Toggle）
        bool canCreateMA = (lastController != null && lastClipCount > 0 && !string.IsNullOrEmpty(parameterName));
        GUI.enabled = canCreateMA;
        if (GUILayout.Button("创建 MA 预制件及开关", GUILayout.Height(38)))
        {
            CreateMAPrefabWithSubMenu();
        }
        GUI.enabled = true;

        // 3) 定位到基础文件夹
        if (GUILayout.Button("定位到生成的动画器文件夹", GUILayout.Height(30)))
        {
            EnsureFolder(BaseFolder);
            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(BaseFolder));
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(BaseFolder);
        }

        EditorGUILayout.HelpBox(string.IsNullOrEmpty(statusMessage) ? "准备就绪。" : statusMessage, MessageType.Info);
    }

    private void DrawDragArea()
    {
        var dragRect = GUILayoutUtility.GetRect(0, 70, GUILayout.ExpandWidth(true));
        GUI.Box(dragRect, "把 AnimationClip 或文件夹拖到这里", EditorStyles.helpBox);
        var evt = Event.current;
        if (!dragRect.Contains(evt.mousePosition)) return;
        switch (evt.type)
        {
            case EventType.DragUpdated:
            case EventType.DragPerform:
                bool hasValid = DragAndDrop.objectReferences.Any(o => o is AnimationClip || IsProjectFolder(o));
                DragAndDrop.visualMode = hasValid ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;
                if (evt.type == EventType.DragPerform && hasValid)
                {
                    DragAndDrop.AcceptDrag();
                    foreach (var obj in DragAndDrop.objectReferences)
                    {
                        if (obj is AnimationClip clip && clip != null)
                        {
                            if (!clips.Contains(clip)) clips.Add(clip);
                        }
                        else if (IsProjectFolder(obj))
                        {
                            string folder = AssetDatabase.GetAssetPath(obj);
                            AddClipsFromFolderRecursive(folder);
                        }
                    }
                }
                Event.current.Use();
                break;
        }
    }

    private static bool IsProjectFolder(UnityEngine.Object obj)
    {
        if (obj == null) return false;
        string path = AssetDatabase.GetAssetPath(obj);
        return !string.IsNullOrEmpty(path) && AssetDatabase.IsValidFolder(path);
    }

    private void AddClipsFromFolderRecursive(string folderPath)
    {
        var guids = AssetDatabase.FindAssets("t:AnimationClip", new[] { folderPath });
        foreach (var guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip != null && !clips.Contains(clip)) clips.Add(clip);
        }
    }

    // —— 构建控制器 ——
    private void CreateController()
    {
        string sanitizedControllerName = SanitizeFileName(controllerName);
        string controllerFolder = $"{BaseFolder}/{sanitizedControllerName}";
        EnsureFolder(controllerFolder);

        var validClips = clips.Where(c => c != null).ToList();
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

        // 可选：在门控/退出前状态上附加 VRC 行为（保持你之前的逻辑）
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
            $"Animator 创建成功：{Path.GetFileName(targetPath)}\n动画 {states.Count} \n过渡 exitTime={transitionsExitTime}, duration={transitionsDuration}s\n" +
            $"空1：{info1a} \n {info1b}\n空2：{info2a} \n {info2b}";

        // 自动定位到生成的文件夹
        EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(controllerFolder));
        Selection.activeObject = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(controllerFolder);
    }

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

    // =========================
    // 2) 创建 MA 预制件（父级 SubMenu / Children，子级 Toggle）
    // =========================
    private void CreateMAPrefabWithSubMenu()
    {
        if (lastController == null || lastClipCount <= 0)
        {
            statusMessage = "请先创建动画控制器。";
            return;
        }

        string sanitizedControllerName = SanitizeFileName(controllerName);
        string prefabFolder = $"{BaseFolder}/{sanitizedControllerName}";
        EnsureFolder(prefabFolder);

        // 根对象
        var rootGO = new GameObject("动作包MA");

        // —— 合并 Animator 到 Action 层（使用绝对路径模式，动画层合并优先级设置为9） ——
        var merge = rootGO.AddComponent<ModularAvatarMergeAnimator>();
        merge.animator = lastController;
        merge.layerType = VRCAvatarDescriptor.AnimLayerType.Action;

        // 设置为绝对路径模式
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

        // 设置动画层合并优先级为9
        var priorityField = typeof(ModularAvatarMergeAnimator).GetField("layerPriority", BindingFlags.Public | BindingFlags.Instance);
        if (priorityField != null)
        {
            priorityField.SetValue(merge, 9);
        }

        // —— 父级子菜单（SubMenu / Children）并安装到 Avatar ——
        var menuRoot = new GameObject("动作菜单");
        menuRoot.transform.SetParent(rootGO.transform, false);

        var parentItem = menuRoot.AddComponent<ModularAvatarMenuItem>();
        parentItem.automaticValue = true;
        parentItem.Control.type = VRCExpressionsMenu.Control.ControlType.SubMenu;   // 类型：子菜单
        parentItem.MenuSource = SubmenuSource.Children;                           // 子菜单来源：Children
        menuRoot.AddComponent<ModularAvatarMenuInstaller>();                        // 绑定安装器

        // —— Parameters：声明 Int 参数（Saved× / Synced√ 由 MA 处理） ——
        var mp = menuRoot.AddComponent<ModularAvatarParameters>();
        var pc = new ParameterConfig
        {
            nameOrPrefix = string.IsNullOrEmpty(parameterName) ? "pose" : parameterName,
            syncType = ParameterSyncType.Int,
            saved = false
        };
        mp.parameters.Add(pc);

        // —— 子项：为每个动画创建 Toggle（参数=pose；值按数量递增） ——
        for (int i = 0; i < lastClipCount; i++)
        {
            int value = lastStartIndex + i;
            var itemGO = new GameObject($"动作_{value}");
            itemGO.transform.SetParent(menuRoot.transform, false); // 成为父级的子对象，Children 将收集

            var mi = itemGO.AddComponent<ModularAvatarMenuItem>();
            mi.Control.type = VRCExpressionsMenu.Control.ControlType.Toggle;
            mi.Control.name = itemGO.name;
            mi.Control.parameter = new VRCExpressionsMenu.Control.Parameter { name = pc.nameOrPrefix };

            // 设置为自动模式，不填入具体值
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
        }

        // 保存为 Prefab
        string prefabPath = AssetDatabase.GenerateUniqueAssetPath($"{prefabFolder}/{sanitizedControllerName}_MA.prefab");
        PrefabUtility.SaveAsPrefabAsset(rootGO, prefabPath, out bool prefabSuccess);
        UnityEngine.Object.DestroyImmediate(rootGO);

        statusMessage = prefabSuccess
            ? $"MA 预制件创建成功：{Path.GetFileName(prefabPath)}（父级 SubMenu/Children，子项 {lastClipCount} 个 Toggle）"
            : "创建 MA 预制件失败。";
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkAllScenesDirty();

        // 更新最后生成的文件夹路径
        lastGeneratedFolderPath = prefabFolder;

        // 自动定位到生成的文件夹
        EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(prefabFolder));
        Selection.activeObject = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(prefabFolder);
    }

    // =========================
    // VRChat Behaviours（保持不变）
    // =========================
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

    // =========================
    // 底层序列化辅助
    // =========================
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

    // —— 工具方法 ——
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

    private static string SanitizeFileName(string name)
    {
        if (string.IsNullOrEmpty(name)) return "PoseAnimatorController";
        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c.ToString(), "_");
        return name;
    }

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
