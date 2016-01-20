﻿#region Copyright (c) 2015 KEngine / Kelly <http://github.com/mr-kelly>, All rights reserved.

// KEngine - Toolset and framework for Unity3D
// ===================================
// 
// Filename: KUIModule.cs
// Date:     2015/12/03
// Author:  Kelly
// Email: 23110388@qq.com
// Github: https://github.com/mr-kelly/KEngine
// 
// This library is free software; you can redistribute it and/or
// modify it under the terms of the GNU Lesser General Public
// License as published by the Free Software Foundation; either
// version 3.0 of the License, or (at your option) any later version.
// 
// This library is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
// Lesser General Public License for more details.
// 
// You should have received a copy of the GNU Lesser General Public
// License along with this library.

#endregion

using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using KEngine;
using UnityEngine;

/// <summary>
/// UI Module
/// </summary>
[CDependencyClass(typeof (KResourceModule))]
public class KUIModule : KEngine.IModule
{
    private class _InstanceClass
    {
        public static KUIModule _Instance = new KUIModule();
    }

    public static KUIModule Instance
    {
        get { return _InstanceClass._Instance; }
    }

    /// <summary>
    /// 正在加载的UI统计
    /// </summary>
    private int _loadingUICount = 0;

    public int LoadingUICount
    {
        get { return _loadingUICount; }
        set
        {
            _loadingUICount = value;
            if (_loadingUICount < 0) Logger.LogError("Error ---- LoadingUICount < 0");
        }
    }


    /// <summary>
    /// A bridge for different UI System, for instance, you can use NGUI or EZGUI or etc.. UI Plugin through UIBridge
    /// </summary>
    public IKUIBridge UiBridge;

    public Dictionary<string, CUILoadState> UIWindows = new Dictionary<string, CUILoadState>();
    public bool UIRootLoaded = false;

    public static event Action<KUIController> OnInitEvent;

    public static event Action<KUIController> OnOpenEvent;
    public static event Action<KUIController> OnCloseEvent;

    private KUIModule()
    {
    }

    public IEnumerator Init()
    {
        var configUiBridge = AppEngine.GetConfig("UIModuleBridge");

        if (!string.IsNullOrEmpty(configUiBridge))
        {
            var uiBridgeTypeName = string.Format("K{0}Bridge", configUiBridge);
            var uiBridgeType = Type.GetType(uiBridgeTypeName);
            if (uiBridgeType != null)
            {
                UiBridge = Activator.CreateInstance(uiBridgeType) as IKUIBridge;
                Logger.Debug("Use UI Bridge: {0}", uiBridgeType);
            }
            else
            {
                Logger.LogError("Cannot find UIBridge Type: {0}", uiBridgeTypeName);
            }
        }

        if (UiBridge == null)
        {
            UiBridge = new KUGUIBridge();
        }

        UiBridge.InitBridge();

        yield break;
    }

    public IEnumerator UnInit()
    {
        yield break;
    }

    public CUILoadState OpenWindow(Type type, params object[] args)
    {
        string uiName = type.Name.Remove(0, 3); // 去掉"CUI"
        return OpenWindow(uiName, args);
    }

    public CUILoadState OpenWindow<T>(params object[] args) where T : KUIController
    {
        return OpenWindow(typeof (T), args);
    }

    // 打开窗口（非复制）
    public CUILoadState OpenWindow(string name, params object[] args)
    {
        CUILoadState uiState;
        if (!UIWindows.TryGetValue(name, out uiState))
        {
            uiState = LoadWindow(name, true, args);
            return uiState;
        }

        OnOpen(uiState, args);
        return uiState;
    }

    // 隐藏时打开，打开时隐藏
    public void ToggleWindow<T>(params object[] args)
    {
        string uiName = typeof (T).Name.Remove(0, 3); // 去掉"CUI"
        ToggleWindow(uiName, args);
    }

    public void ToggleWindow(string name, params object[] args)
    {
        if (IsOpen(name))
        {
            CloseWindow(name);
        }
        else
        {
            OpenWindow(name, args);
        }
    }


    /// <summary>
    /// // Dynamic动态窗口，复制基准面板
    /// </summary>
    public CUILoadState OpenDynamicWindow(string template, string instanceName, params object[] args)
    {
        CUILoadState uiState = _GetUIState(instanceName);
        if (uiState != null)
        {
            OnOpen(uiState, args);
            return uiState;
        }

        CUILoadState uiInstanceState;
        if (!UIWindows.TryGetValue(instanceName, out uiInstanceState)) // 实例创建
        {
            uiInstanceState = new CUILoadState(template, instanceName);
            uiInstanceState.IsStaticUI = false;
            uiInstanceState.IsLoading = true;
            uiInstanceState.UIWindow = null;
            uiInstanceState.OpenWhenFinish = true;
            UIWindows[instanceName] = uiInstanceState;
        }

        CallUI(template, (_ui, _args) =>
        {
            // _args useless

            CUILoadState uiTemplateState = _GetUIState(template);

            // 组合template和name的参数 和args外部参数
            object[] totalArgs = new object[args.Length + 2];
            totalArgs[0] = template;
            totalArgs[1] = instanceName;
            args.CopyTo(totalArgs, 2);

            OnDynamicWindowCallback(uiTemplateState.UIWindow, totalArgs);
        });

        return uiInstanceState;
    }

    private void OnDynamicWindowCallback(KUIController _ui, object[] _args)
    {
        string template = (string) _args[0];
        string name = (string) _args[1];

        GameObject uiObj = (GameObject) UnityEngine.Object.Instantiate(_ui.gameObject);

        uiObj.name = name;

        UiBridge.UIObjectFilter(_ui, uiObj);

        CUILoadState instanceUIState = UIWindows[name];
        instanceUIState.IsLoading = false;

        KUIController uiBase = uiObj.GetComponent<KUIController>();
        uiBase.UITemplateName = template;
        uiBase.UIName = name;

        instanceUIState.UIWindow = uiBase;

        object[] originArgs = new object[_args.Length - 2]; // 去除前2个参数
        for (int i = 2; i < _args.Length; i++)
            originArgs[i - 2] = _args[i];
        InitWindow(instanceUIState, uiBase, instanceUIState.OpenWhenFinish, originArgs);
    }

    public void CloseWindow(Type t)
    {
        CloseWindow(t.Name.Remove(0, 3)); // XUI remove
    }

    public void CloseWindow<T>()
    {
        CloseWindow(typeof (T));
    }

    public void CloseWindow(string name)
    {
        CUILoadState uiState;
        if (!UIWindows.TryGetValue(name, out uiState))
        {
            if (Debug.isDebugBuild)
                Logger.LogWarning("[CloseWindow]没有加载的UIWindow: {0}", name);
            return; // 未开始Load
        }

        if (uiState.IsLoading) // Loading中
        {
            if (Debug.isDebugBuild)
                Logger.Log("[CloseWindow]IsLoading的{0}", name);
            uiState.OpenWhenFinish = false;
            return;
        }

        Action doCloseAction = () =>
        {
            uiState.UIWindow.gameObject.SetActive(false);

            uiState.UIWindow.OnClose();

            if (OnCloseEvent != null)
                OnCloseEvent(uiState.UIWindow);

            if (!uiState.IsStaticUI)
            {
                DestroyWindow(name);
            }
        };

        doCloseAction();
    }

    /// <summary>
    /// Destroy all windows that has LoadState.
    /// Be careful to use.
    /// </summary>
    public void DestroyAllWindows()
    {
        List<string> LoadList = new List<string>();

        foreach (KeyValuePair<string, CUILoadState> uiWindow in UIWindows)
        {
            if (IsLoad(uiWindow.Key))
            {
                LoadList.Add(uiWindow.Key);
            }
        }

        foreach (string item in LoadList)
            DestroyWindow(item);
    }

    [Obsolete("Deprecated: Please don't use this")]
    public void CloseAllWindows()
    {
        List<string> toCloses = new List<string>();

        foreach (KeyValuePair<string, CUILoadState> uiWindow in UIWindows)
        {
            if (IsOpen(uiWindow.Key))
            {
                toCloses.Add(uiWindow.Key);
            }
        }

        for (int i = toCloses.Count - 1; i >= 0; i--)
        {
            CloseWindow(toCloses[i]);
        }
    }

    private CUILoadState _GetUIState(string name)
    {
        CUILoadState uiState;
        UIWindows.TryGetValue(name, out uiState);
        if (uiState != null)
            return uiState;

        return null;
    }

    private KUIController GetUIBase(string name)
    {
        CUILoadState uiState;
        UIWindows.TryGetValue(name, out uiState);
        if (uiState != null && uiState.UIWindow != null)
            return uiState.UIWindow;

        return null;
    }

    public bool IsOpen<T>() where T : KUIController
    {
        string uiName = typeof (T).Name.Remove(0, 3); // 去掉"CUI"
        return IsOpen(uiName);
    }

    public bool IsOpen(string name)
    {
        KUIController uiBase = GetUIBase(name);
        return uiBase == null ? false : uiBase.gameObject.activeSelf;
    }

    public bool IsLoad(string name)
    {
        if (UIWindows.ContainsKey(name))
            return true;
        return false;
    }

    public CUILoadState LoadWindow(string windowTemplateName, bool openWhenFinish, params object[] args)
    {
        if (UIWindows.ContainsKey(windowTemplateName))
        {
            Logger.LogError("[LoadWindow]多次重复LoadWindow: {0}", windowTemplateName);
        }
        Logger.Assert(!UIWindows.ContainsKey(windowTemplateName));

        CUILoadState openState = new CUILoadState(windowTemplateName, windowTemplateName);
        openState.IsStaticUI = true;
        openState.OpenArgs = args;

        //if (openState.IsLoading)
        openState.OpenWhenFinish = openWhenFinish;

        KResourceModule.Instance.StartCoroutine(LoadUIAssetBundle(windowTemplateName, openState));

        UIWindows.Add(windowTemplateName, openState);

        return openState;
    }

    private IEnumerator LoadUIAssetBundle(string name, CUILoadState openState)
    {
        LoadingUICount++;

        // 具体加载逻辑
        // manifest
        string manifestPath = string.Format("BundleResources/NGUI/{0}.prefab.manifest{1}", name,
            AppEngine.GetConfig(KEngineDefaultConfigs.AssetBundleExt));
        var manifestLoader = KBytesLoader.Load(manifestPath, KResourceInAppPathType.ResourcesAssetsPath, KAssetBundleLoaderMode.ResourcesLoad);
        while (!manifestLoader.IsCompleted)
            yield return null;
        var manifestBytes = manifestLoader.Bytes;
        manifestLoader.Release(); // 释放掉文本字节
        var utf8NoBom = new UTF8Encoding(false);
        var manifestList = utf8NoBom.GetString(manifestBytes).Split(new char[] {'\n'}, StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < manifestList.Length; i++)
        {
            var depPath = manifestList[i] + AppEngine.GetConfig(KEngineDefaultConfigs.AssetBundleExt);
            var depLoader = KAssetFileLoader.Load(depPath);
            while (!depLoader.IsCompleted)
            {
                yield return null;
            }

        }
        string path = string.Format("BundleResources/NGUI/{0}.prefab{1}", name, KEngine.AppEngine.GetConfig("AssetBundleExt"));

        var assetLoader = KStaticAssetLoader.Load(path);
        openState.UIResourceLoader = assetLoader; // 基本不用手工释放的
        while (!assetLoader.IsCompleted)
            yield return null;

        GameObject uiObj = (GameObject) assetLoader.TheAsset;
        // 具体加载逻辑结束...这段应该放到Bridge里

        uiObj.SetActive(false);
        uiObj.name = openState.TemplateName;

        KUIController uiBase = (KUIController) uiObj.AddComponent(openState.UIType);

        openState.UIWindow = uiBase;

        uiBase.UIName = uiBase.UITemplateName = openState.TemplateName;

        UiBridge.UIObjectFilter(uiBase, uiObj);

        openState.IsLoading = false; // Load完
        InitWindow(openState, uiBase, openState.OpenWhenFinish, openState.OpenArgs);

        LoadingUICount--;
    }

    public void DestroyWindow(string name)
    {
        CUILoadState uiState;
        UIWindows.TryGetValue(name, out uiState);
        if (uiState == null || uiState.UIWindow == null)
        {
            Logger.Log("{0} has been destroyed", name);
            return;
        }

        UnityEngine.Object.Destroy(uiState.UIWindow.gameObject);

        uiState.UIWindow = null;

        UIWindows.Remove(name);
    }

    /// <summary>
    /// 等待并获取UI实例，执行callback
    /// 源起Loadindg UI， 在加载过程中，进度条设置方法会失效
    /// 如果是DynamicWindow,，使用前务必先要Open!
    /// </summary>
    /// <param name="uiTemplateName"></param>
    /// <param name="callback"></param>
    /// <param name="args"></param>
    public void CallUI(string uiTemplateName, Action<KUIController, object[]> callback, params object[] args)
    {
        Logger.Assert(callback);

        CUILoadState uiState;
        if (!UIWindows.TryGetValue(uiTemplateName, out uiState))
        {
            uiState = LoadWindow(uiTemplateName, false); // 加载，这样就有UIState了, 但注意因为没参数，不要随意执行OnOpen
        }

        uiState.DoCallback(callback, args);
    }

    /// <summary>
    /// DynamicWindow专用, 不会自动加载，会提示报错
    /// </summary>
    /// <param name="uiName"></param>
    /// <param name="callback"></param>
    /// <param name="args"></param>
    public void CallDynamicUI(string uiName, Action<KUIController, object[]> callback, params object[] args)
    {
        Logger.Assert(callback);

        CUILoadState uiState;
        if (!UIWindows.TryGetValue(uiName, out uiState))
        {
            Logger.LogError("找不到UIState: {0}", uiName);
            return;
        }

        CUILoadState openState = UIWindows[uiName];
        openState.DoCallback(callback, args);
    }

    public void CallUI<T>(Action<T> callback) where T : KUIController
    {
        CallUI<T>((_ui, _args) => callback(_ui));
    }

    // 使用泛型方式
    public void CallUI<T>(Action<T, object[]> callback, params object[] args) where T : KUIController
    {
        string uiName = typeof (T).Name.Remove(0, 3); // 去掉 "XUI"

        CallUI(uiName, (KUIController _uibase, object[] _args) => { callback((T) _uibase, _args); }, args);
    }

    private void OnOpen(CUILoadState uiState, params object[] args)
    {
        if (uiState.IsLoading)
        {
            uiState.OpenWhenFinish = true;
            uiState.OpenArgs = args;
            return;
        }

        KUIController uiBase = uiState.UIWindow;

        Action doOpenAction = () =>
        {
            if (uiBase.gameObject.activeSelf)
            {
                uiBase.OnClose();
            }

            uiBase.BeforeOpen(args, () =>
            {
                uiBase.gameObject.SetActive(true);

                uiBase.OnOpen(args);

                if (OnOpenEvent != null)
                    OnOpenEvent(uiBase);
            });
        };

        doOpenAction();
    }


    private void InitWindow(CUILoadState uiState, KUIController uiBase, bool open, params object[] args)
    {
        uiBase.OnInit();
        if (OnInitEvent != null)
            OnInitEvent(uiBase);
        if (open)
        {
            OnOpen(uiState, args);
        }

        if (!open)
        {
            if (!uiState.IsStaticUI)
            {
                CloseWindow(uiBase.UIName); // Destroy
                return;
            }
            else
            {
                uiBase.gameObject.SetActive(false);
            }
        }

        uiState.OnUIWindowLoadedCallbacks(uiState, uiBase);
    }
}

/// <summary>
/// UI Async Load State class
/// </summary>
public class CUILoadState
{
    public string TemplateName;
    public string InstanceName;
    public KUIController UIWindow;
    public string UIType;
    public bool IsLoading;
    public bool IsStaticUI; // 非复制出来的, 静态UI

    public bool OpenWhenFinish;
    public object[] OpenArgs;

    internal Queue<Action<KUIController, object[]>> CallbacksWhenFinish;
    internal Queue<object[]> CallbacksArgsWhenFinish;
    public KAbstractResourceLoader UIResourceLoader; // 加载器，用于手动释放资源

    public CUILoadState(string uiTypeTemplateName, string uiInstanceName)
    {
        TemplateName = uiTypeTemplateName;
        InstanceName = uiInstanceName;
        UIWindow = null;
        UIType = "KUI" + uiTypeTemplateName;

        IsLoading = true;
        OpenWhenFinish = false;
        OpenArgs = null;

        CallbacksWhenFinish = new Queue<Action<KUIController, object[]>>();
        CallbacksArgsWhenFinish = new Queue<object[]>();
    }


    /// <summary>
    /// 确保加载完成后的回调
    /// </summary>
    /// <param name="callback"></param>
    /// <param name="args"></param>
    public void DoCallback(Action<KUIController, object[]> callback, object[] args = null)
    {
        if (args == null)
            args = new object[0];

        if (IsLoading) // Loading
        {
            CallbacksWhenFinish.Enqueue(callback);
            CallbacksArgsWhenFinish.Enqueue(args);
            return;
        }

        // 立即执行即可
        callback(UIWindow, args);
    }

    internal void OnUIWindowLoadedCallbacks(CUILoadState uiState, KUIController uiObject)
    {
        //if (openState.OpenWhenFinish)  // 加载完打开 模式下，打开时执行回调
        {
            while (uiState.CallbacksWhenFinish.Count > 0)
            {
                Action<KUIController, object[]> callback = uiState.CallbacksWhenFinish.Dequeue();
                object[] _args = uiState.CallbacksArgsWhenFinish.Dequeue();
                //callback(uiBase, _args);

                DoCallback(callback, _args);
            }
        }
    }
}