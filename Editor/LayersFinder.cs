using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Reflection;
using System;
using System.Text;
using UnityEditor.PackageManager.UI;

public class LayersFinder : EditorWindow
{
    private string _newLayerName = "";
    private string _layerName = "";
    private SearchMode _currentSearchMode = SearchMode.Files;

    public class AssetReport
    {
        public GameObject ObjectRef;
        public string ObjectName;
        public string FilePath;
        public string ObjectPath;
        public string CustomMessage = string.Empty;
        public Action<AssetReport> OnClickAction;
    }

    private Dictionary<string, List<AssetReport>> _layerReport = new();
    private Dictionary<string, bool> _layerFoldouts = new();
    private Vector2 _scrollPos;
    private bool _includeChildrenObjects = false;
    private bool _reportGenerated = false;
    private string _debugLog = string.Empty;
    private Color _debugColor;
    private Color _logColor = Color.white;
    private Color _warningColor = new Color(254, 153, 0);
    private Color _errorColor = Color.red;
    private bool _doFoldouts = false;
    private static Texture _windowIcon;

    private enum SearchMode
    {
        None = 0,
        Scene = 1,
        Files = 2, 
        Layermask = 3,
        Report = 4,
        Rename = 5
    }

    private Dictionary<SearchMode, string> _modeActionTexts = new Dictionary<SearchMode, string>()
    {
        {SearchMode.Scene, "Search" },
        {SearchMode.Files, "Search" },
        {SearchMode.Layermask, "Find in Monobehaviors" },
        {SearchMode.Report, "Generate Report" },
        {SearchMode.Rename, "Rename" }
    };

    [MenuItem("Tools/Layers Finder")]
    public static void ShowWindow()
    {
        _windowIcon = (Texture2D)EditorGUIUtility.Load("T_LayersIcon.png");
        var window  = GetWindow<LayersFinder>();
        window.titleContent = new GUIContent("Layers Finder", _windowIcon);
    }

    private void OnGUI()
    {
        GUILayout.Space(20);
        _layerName = EditorGUILayout.TextField("Layer Name", _layerName);

        GUILayout.BeginHorizontal();
        _currentSearchMode = GetToggleOption("Scene Search", 
            "Search through the scene for objects on a given layer", SearchMode.Scene);
        _currentSearchMode = GetToggleOption("Files Search", "", SearchMode.Files);
        _currentSearchMode = GetToggleOption("Layermask References", "", SearchMode.Layermask);
        _currentSearchMode = GetToggleOption("Report", "", SearchMode.Report);
        _currentSearchMode = GetToggleOption("Rename Layer", "", SearchMode.Rename);
        GUILayout.EndHorizontal();

        string buttonText = _modeActionTexts[_currentSearchMode];
        Action action = null;

        EditorGUILayout.BeginVertical("HelpBox");
        GUILayout.Label("Settings", EditorStyles.boldLabel);
        GUILayout.Space(10);

        switch (_currentSearchMode)
        {
            case SearchMode.Scene:
                action = FindInScene;
                break;
            case SearchMode.Files:
                action = FindInFiles;
                break;
            case SearchMode.Layermask:
                action = FindInMonoBehaviours;
                break;
            case SearchMode.Report:
                _includeChildrenObjects = GUILayout.Toggle(_includeChildrenObjects, "Include children objects");

                action = GenerateLayerReport;
                break;
            case SearchMode.Rename:
                _newLayerName = EditorGUILayout.TextField("New Layer Name", _newLayerName);

                action = ChangeLayerInFiles;
                break;
        }

        GUILayout.Space(20);

        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
    
        if(GUILayout.Button(buttonText, GUILayout.MinWidth(100), GUILayout.MaxWidth(400), GUILayout.MaxHeight(25)))
        {
            action?.Invoke();
        }

        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        if(_debugLog.Length > 0)
        {
            if(GUILayout.Button("Clear"))
            {
                _debugLog = string.Empty;
            }
        }

        GUI.contentColor = _debugColor;
        GUILayout.Label(_debugLog);
        GUI.contentColor = Color.white;
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
        GUILayout.Space(5);
        EditorGUILayout.EndVertical();

        RenderReportAreaOnGUI();
    }

    private void FindInScene()
    {
        if (!LayerExists(_layerName))
        {
            PrintDebug("Layer not found: " + _layerName, _errorColor);
            return;
        }

        _layerReport.Clear();

        int layerIndex = LayerMask.NameToLayer(_layerName);
        GameObject[] allObjects = GameObject.FindObjectsOfType<GameObject>(true);
        bool found = false;

        foreach (GameObject obj in allObjects)
        {
            if (obj.layer == layerIndex)
            {
                AddToLayerReport(obj, string.Empty);
                found = true;
            }
        }

        if (!found)
        {
            PrintDebug("No matches found in Scene for layer: " + _layerName, _warningColor);
        }
    }

    private void FindInFiles()
    {
        _layerReport.Clear();
        _doFoldouts = false;

        if (!LayerExists(_layerName))
        {
            PrintDebug("Layer not found: " + _layerName, _errorColor);
            return;
        }

        int layerIndex = LayerMask.NameToLayer(_layerName);
        string[] guids = AssetDatabase.FindAssets("t:GameObject");
        bool found = false;

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);

            if (prefab != null)
            {
                var transforms = prefab.GetComponentsInChildren<Transform>(true);

                foreach (Transform child in transforms)
                {
                    if (child.gameObject.layer == layerIndex)
                    {
                        AddToLayerReport(child.gameObject, path);
                        found = true;
                    }
                }
            }
        }

        if (!found)
        {
            PrintDebug("No matches found in Project Files for layer: " + _layerName, _warningColor);
        }
    }

    private void FindInMonoBehaviours()
    {
        if (!LayerExists(_layerName))
        {
            PrintDebug("Layer not found: " + _layerName, _errorColor);
            return;
        }

        int layerIndex = LayerMask.NameToLayer(_layerName);
        GameObject[] allObjects = GameObject.FindObjectsOfType<GameObject>(true);
        string[] guids = AssetDatabase.FindAssets("t:GameObject");
        bool found = false;

        foreach (GameObject obj in allObjects)
        {
            if (CheckMonoBehaviours(obj, layerIndex, "Scene", GetFullPath(obj.transform)))
            {
                found = true;
            }
        }

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);

            if (prefab != null)
            {
                foreach (Transform child in prefab.GetComponentsInChildren<Transform>(true))
                {
                    if (CheckMonoBehaviours(child.gameObject, layerIndex, "Asset", GetFullPath(child) + " | Path: " + path))
                    {
                        found = true;
                    }
                }
            }
        }

        if (!found)
        {
            PrintDebug("No MonoBehaviour layer mask matches found for layer: " + _layerName, _warningColor);
        }
    }

    private bool CheckMonoBehaviours(GameObject obj, int targetLayer, string context, string identifier)
    {
        bool found = false;
        MonoBehaviour[] behaviours = obj.GetComponents<MonoBehaviour>();

        foreach (MonoBehaviour behaviour in behaviours)
        {
            if (behaviour == null)
            {
                continue;
            }

            FieldInfo[] fields = behaviour.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            foreach (FieldInfo field in fields)
            {
                if (field.FieldType == typeof(LayerMask))
                {
                    LayerMask mask = (LayerMask)field.GetValue(behaviour);

                    if (mask.value != ~0 && ((1 << targetLayer) & mask.value) != 0)
                    {
                        Debug.Log($"{ColorText(context, Color.cyan)} " +
                            $"MonoBehaviour Match: {ColorText(behaviour.GetType().Name, Color.green)} on " +
                            $"{ColorText(identifier, Color.yellow)} references layer {ColorText(LayerMask.LayerToName(targetLayer), Color.red)}");
                        found = true;
                    }
                }
            }
        }

        return found;
    }

    private void ChangeLayerInFiles()
    {
        if (!LayerExists(_layerName))
        {
            PrintDebug("Source layer not found: " + _layerName, _errorColor);
            return;
        }

        int sourceLayer = LayerMask.NameToLayer(_layerName);
        int newLayer = LayerMask.NameToLayer(_newLayerName);
        string[] guids = AssetDatabase.FindAssets("t:GameObject");

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);

            if (prefab != null)
            {
                bool changed = false;
                foreach (Transform child in prefab.GetComponentsInChildren<Transform>(true))
                {
                    if (child.gameObject.layer == sourceLayer)
                    {
                        string heirarchyPath = GetFullPath(child);

                        AssetReport report = new AssetReport()
                        {
                            ObjectRef = child.gameObject,
                            CustomMessage = $"<color=#7DDA58>Changed Layer: {_layerName} -> {_newLayerName}</color>",
                            FilePath = $"File path: {path}",
                            OnClickAction = PingInAssets,
                            ObjectName = child.gameObject.name,
                            ObjectPath = heirarchyPath
                        };

                        AddToLayerReport(report, child.gameObject);

                        child.gameObject.layer = newLayer;
                        changed = true;
                    }
                }

                if (changed)
                {
                    EditorUtility.SetDirty(prefab);
                }
            }
        }

        AssetDatabase.SaveAssets();
    }

    private void RenderReportAreaOnGUI()
    {
        EditorGUILayout.BeginVertical(GUILayout.ExpandHeight(true));
        EditorGUILayout.Space();
        GUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Layer Report", EditorStyles.boldLabel);
        GUILayout.FlexibleSpace();
        if(GUILayout.Button("Clear Results"))
        {
            _layerReport.Clear();
            _layerFoldouts.Clear();
        }
        GUILayout.EndHorizontal();
        EditorGUILayout.BeginVertical("HelpBox", GUILayout.ExpandHeight(true));

        _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos, GUILayout.ExpandHeight(true));

        RenderReportAreaBox();

        EditorGUILayout.EndScrollView();

        EditorGUILayout.EndVertical();
        EditorGUILayout.EndVertical();
    }

    private void RenderReportAreaBox()
    {
        if (_reportGenerated)
        {
            EditorGUILayout.LabelField($"\nReport of game objects based on layers {(_includeChildrenObjects ? "with children gameObjects" : "")}:\n");
        }

        for (int i = 0; i < 32; i++)
        {
            string layer = LayerMask.LayerToName(i);

            // skip layer indices that don't exist
            if (string.IsNullOrEmpty(layer) || !_layerReport.ContainsKey(layer))
            {
                continue;
            }

            if (_doFoldouts)
            {
                int count = _layerReport[layer].Count;
                _layerFoldouts.TryAdd(layer, false);
                _layerFoldouts[layer] = EditorGUILayout.Foldout(_layerFoldouts[layer], $"{i:00}: {layer} ({count})", true);

                if (_layerFoldouts[layer] && count > 0)
                {
                    EditorGUI.indentLevel++;
                    RenderLayerReport(layer);
                    EditorGUI.indentLevel--;
                }
            }
            else
            {
                RenderLayerReport(layer);
            }
        }
    }

    private void RenderLayerReport(string layer)
    {
        foreach (var report in _layerReport[layer])
        {
            var pathObjs = report.ObjectPath.Split('/');
            string debug = string.Empty;
            Array.ForEach(pathObjs, o => debug += $"{o}/");

            string displayName = report.ObjectPath == report.ObjectName
            ? report.ObjectName
            : $"{pathObjs[0]} ({report.ObjectName})";

            int childrenCount = pathObjs.Length;

            if (GUILayout.Button(displayName, EditorStyles.linkLabel))
            {
                report.OnClickAction?.Invoke(report);
            }

            EditorGUI.indentLevel++;

            var style = new GUIStyle(EditorStyles.label);
            style.richText = true;

            if (report.ObjectPath.Length > 0 && childrenCount > 1)
                EditorGUILayout.LabelField($"<b>Hierarchy path</b>: {report.ObjectPath}", style);
            if (report.FilePath.Length > 0)
                EditorGUILayout.LabelField($"<b>File path</b>: {report.FilePath}", style);
            if (report.CustomMessage.Length > 0)
                EditorGUILayout.LabelField(report.CustomMessage, style);
            EditorGUILayout.Space(2);
            EditorGUI.indentLevel--;
        }
    }

    private void GenerateLayerReport()
    {
        _layerReport.Clear();
        _layerFoldouts.Clear();
        _doFoldouts = true;
        _reportGenerated = true;

        for (int i = 0; i < 32; i++)
        {
            string layerName = LayerMask.LayerToName(i);

            if (!string.IsNullOrEmpty(layerName))
            {
                _layerReport[layerName] = new List<AssetReport>();
            }
        }

        string[] guids = AssetDatabase.FindAssets("t:GameObject");

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);

            if (prefab == null)
            {
                continue;
            }

            if (_includeChildrenObjects)
            {
                AddToLayerReport(prefab, path);

                var allTransforms = prefab.GetComponentsInChildren<Transform>(true);

                foreach (Transform child in allTransforms)
                {
                    if (child == prefab.transform)
                    {
                        continue;
                    }

                    AddToLayerReport(child.gameObject, path);
                }
            }
            else
            {
                AddToLayerReport(prefab, path);
            }
        }
    }

    #region Utilities
    private void PrintDebug(string text, Color color)
    {
        _debugLog = text;
        _debugColor = color;
    }

    public string ColorText(string text, Color _color)
    {
        return $"<color=#{ColorUtility.ToHtmlStringRGB(_color)}>{text}</color>";
    }

    private bool LayerExists(string name)
    {
        return LayerMask.NameToLayer(name) != -1;
    }

    private SearchMode GetToggleOption(string labelText, string tooltip, SearchMode targetSearchMode)
    {
        GUIContent content = new GUIContent(labelText, tooltip);
        return EditorGUILayout.ToggleLeft(content, _currentSearchMode == targetSearchMode) ? targetSearchMode : _currentSearchMode;
    }

    private string GetFullPath(Transform transform)
    {
        StringBuilder path = new StringBuilder();
        path.Append(transform.name);
        transform = transform.parent;

        while (transform != null)
        {
            path.Append(transform.name + "/" + path);
            transform = transform.parent;
        }

        return path.ToString();
    }

    private void AddToLayerReport(GameObject obj, string filePath, string customMessage = "")
    {
        string layerName = LayerMask.LayerToName(obj.layer);

        if (string.IsNullOrEmpty(layerName))
        {
            return;
        }

        if (!_layerReport.ContainsKey(layerName))
        {
            _layerReport[layerName] = new List<AssetReport>();
        }

        AssetReport newReport = new AssetReport()
        {
            ObjectRef = obj,
            ObjectName = obj.name,
            ObjectPath = GetFullPath(obj.transform),
            FilePath = filePath,
            OnClickAction = PingInAssets,
            CustomMessage = customMessage
        };

        _layerReport[layerName].Add(newReport);
    }

    private void AddToLayerReport(AssetReport report, GameObject obj)
    {
        string layerName = LayerMask.LayerToName(obj.layer);

        if (string.IsNullOrEmpty(layerName))
        {
            return;
        }

        if (!_layerReport.ContainsKey(layerName))
        {
            _layerReport[layerName] = new List<AssetReport>();
        }

        _layerReport[layerName].Add(report);
    }

    private void PingInAssets(AssetReport report)
    {
        var obj = report.ObjectRef;

        if (obj != null)
        {
            EditorGUIUtility.PingObject(obj);
            Selection.activeObject = obj;
        }
    }

    public static Texture GetIcon(string path)
    {
        Texture texture = AssetDatabase.LoadAssetAtPath<Texture>(path);

        if (texture == null)
        {
            Debug.LogError($"No icon found at path: {path}");
        }

        return texture;
    }
    #endregion
}
