using System;
using System.IO;
using System.Text;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using FM.UI;
using FM.Game;
using SI.Core;
using SI.Bindable;
using SI.Bindable.Reference.Core;

namespace FM26FullPlayerProbe
{
    [BepInPlugin("com.schumy12.fm26.fullplayerprobe", "FM26 Full Player Probe", "0.32.0")]
    public sealed class Plugin : BasePlugin
    {
        internal static new BepInEx.Logging.ManualLogSource Log;
        private static ProbeBehaviour _behaviour;

        public override void Load()
        {
            Log = base.Log;
            Log.LogInfo("[FM26FullProbe] Loaded v0.32 HANDLER TREE DIAGNOSTIC - select one player and press F8.");
            _behaviour = AddComponent<ProbeBehaviour>();
        }

        public override bool Unload()
        {
            if (_behaviour != null) UnityEngine.Object.Destroy(_behaviour);
            return base.Unload();
        }
    }

    public sealed class ProbeBehaviour : MonoBehaviour
    {
        private const uint NameProperty = 1851878757u;
        private const float WaitSeconds = 2.0f;

        private BindingSubsystem _bindings;
        private InteropDataHandler _handler;
        private Bindings.Key _key;
        private TypedValue _source;
        private StringBuilder _sb;
        private bool _waiting;
        private float _checkAt;
        private bool _channelOpen;

        public ProbeBehaviour(IntPtr ptr) : base(ptr) { }

        private void Update()
        {
            try
            {
                if (!_waiting && Keyboard.current != null && Keyboard.current.f8Key.wasPressedThisFrame)
                    StartProbe();
                if (_waiting && Time.unscaledTime >= _checkAt)
                    FinishProbe();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("[FM26FullProbe] Update error: " + ex);
                try { _sb?.AppendLine("UPDATE/FATAL: " + ex); } catch { }
                SaveAndReset();
            }
        }

        private void StartProbe()
        {
            _sb = new StringBuilder();
            _sb.AppendLine("=== FM26 FULL PLAYER PROBE 0.32 HANDLER TREE DIAGNOSTIC ===");
            _sb.AppendLine("0.31 proved the real selected PersonReference is valid and all tested properties are accepted/canHandle, but the global Bindings node never becomes IsDataSet.");
            _sb.AppendLine("This probe checks the InteropDataHandler public BindingTree directly and the GameInteropSubsystem batched request queue.");
            _sb.AppendLine("Property tested: Name=" + NameProperty);
            _sb.AppendLine();

            var showPerson = FindSelectedShowPerson(_sb);
            if (showPerson == null)
            {
                _sb.AppendLine("RESULT: selected ShowPerson NOT FOUND");
                SaveAndReset();
                return;
            }

            try
            {
                BindingPath path;
                ActionGroupings groupings;
                bool multiple;
                var objects = PluginContextMenuContributor.GetContextMenuObjects(showPerson, out path, out groupings, out multiple);
                _sb.AppendLine("GetContextMenuObjects count=" + (objects == null ? -1 : objects.Count) + " multiple=" + multiple);
                if (objects == null || objects.Count == 0)
                {
                    _sb.AppendLine("RESULT: no context objects");
                    SaveAndReset();
                    return;
                }

                _source = objects[0];
                _sb.AppendLine("source type=" + SafeType(_source) + " text='" + SafeText(_source) + "'");
                try
                {
                    var raw = _source.Get();
                    var pr = new PersonReference(raw.Pointer);
                    _sb.AppendLine("REAL PERSON Data1=" + pr.Data1 + " m_index=" + pr.m_index + " combined=" + pr.CombinedIndexAndType + " type=" + pr.Type);
                }
                catch (Exception ex)
                {
                    _sb.AppendLine("person identity read failed: " + ex.GetType().Name + " - " + ex.Message);
                }
            }
            catch (Exception ex)
            {
                _sb.AppendLine("context lookup failed: " + ex.GetType().Name + " - " + ex.Message);
                SaveAndReset();
                return;
            }

            _bindings = EmbeddedDataHandler.s_bindingSubsystem;
            if (_bindings == null)
            {
                _sb.AppendLine("RESULT: BindingSubsystem NOT FOUND");
                SaveAndReset();
                return;
            }

            _handler = FindLiveInteropHandler(_sb, _bindings);
            if (_handler == null)
            {
                _sb.AppendLine("RESULT: InteropDataHandler NOT FOUND");
                SaveAndReset();
                return;
            }

            _sb.AppendLine();
            _sb.AppendLine("=== HANDLER TREE BEFORE OPEN ===");
            DumpHandlerTree(_sb, _handler, default(Bindings.Key), false);

            try
            {
                _key = CreateTemporaryNode(_bindings, "__fm26probe_tree_diag");
                _sb.AppendLine("NODE key=" + _key.m_key + " valid=" + SafeValid(_key) + " globalExists=" + SafeExists(_bindings, ref _key));
            }
            catch (Exception ex)
            {
                _sb.AppendLine("NODE CREATE FAIL: " + ex.GetType().Name + " - " + ex.Message);
                SaveAndReset();
                return;
            }

            DumpHandlerTree(_sb, _handler, _key, true);

            try
            {
                _sb.AppendLine("interop batchedRequests before=" + SafeBatchCount(_handler));
                var property = new Bindings.Property("Name", new PropertyID(NameProperty));
                var contexts = new Il2CppSystem.Collections.Generic.List<string>();
                _sb.AppendLine("schemaAccepts=" + PersonReference.AcceptsPropertyInternal(NameProperty) + " canHandle=" + _handler.CanHandle(_source, property, contexts));
                _handler.OpenChannel(_source, property, _key);
                _channelOpen = true;
                _sb.AppendLine("channel='" + SafeChannelName(_handler, _key) + "'");
                _sb.AppendLine("interop batchedRequests immediatelyAfterOpen=" + SafeBatchCount(_handler));
                DumpHandlerTree(_sb, _handler, _key, true);
            }
            catch (Exception ex)
            {
                _sb.AppendLine("OPEN FAIL: " + ex.GetType().Name + " - " + ex.Message);
                SaveAndReset();
                return;
            }

            _waiting = true;
            _checkAt = Time.unscaledTime + WaitSeconds;
        }

        private void FinishProbe()
        {
            _waiting = false;
            _sb.AppendLine();
            _sb.AppendLine("=== AFTER " + WaitSeconds + "s ===");
            _sb.AppendLine("interop batchedRequests=" + SafeBatchCount(_handler));

            try
            {
                bool exists = _bindings != null && _bindings.Exists(ref _key);
                bool set = _bindings != null && _bindings.IsDataSet(_key);
                _sb.AppendLine("GLOBAL exists=" + exists + " isDataSet=" + set);
                if (exists && set)
                {
                    var value = _bindings.Get(ref _key);
                    _sb.AppendLine("GLOBAL VALUE type=" + SafeType(value) + " text='" + SafeText(value) + "'");
                }
            }
            catch (Exception ex)
            {
                _sb.AppendLine("GLOBAL READ FAIL: " + ex.GetType().Name + " - " + ex.Message);
            }

            DumpHandlerTree(_sb, _handler, _key, true);
            CloseChannel();
            _sb.AppendLine("handler channels after close=" + SafeChannelCount(_handler));
            SaveAndReset();
        }

        private static void DumpHandlerTree(StringBuilder sb, InteropDataHandler handler, Bindings.Key key, bool testKey)
        {
            try
            {
                var tree = handler.BindingTree;
                sb.AppendLine("handler.BindingTree getter OK valid=" + tree.IsValid());
                try { sb.AppendLine("handler tree DataSet count=" + (tree.DataSet == null ? -1 : tree.DataSet.Count)); }
                catch (Exception ex) { sb.AppendLine("handler tree DataSet read fail: " + ex.GetType().Name + " - " + ex.Message); }

                if (testKey)
                {
                    try
                    {
                        var tv = tree.Get(ref key);
                        sb.AppendLine("handler tree Get(key) => " + (tv == null ? "<null>" : (SafeType(tv) + " '" + SafeText(tv) + "'")));
                    }
                    catch (Exception ex)
                    {
                        sb.AppendLine("handler tree Get(key) FAIL: " + ex.GetType().Name + " - " + ex.Message);
                    }

                    try
                    {
                        var node = tree.GetNode(ref key);
                        sb.AppendLine("handler tree GetNode(key) => " + (node == null ? "<null>" : "FOUND"));
                    }
                    catch (Exception ex)
                    {
                        sb.AppendLine("handler tree GetNode(key) FAIL: " + ex.GetType().Name + " - " + ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine("handler.BindingTree getter FAIL: " + ex.GetType().Name + " - " + ex.Message);
            }
        }

        private static int SafeBatchCount(InteropDataHandler handler)
        {
            try { return handler == null || handler.m_interop == null || handler.m_interop.m_batchedRequests == null ? -1 : handler.m_interop.m_batchedRequests.Count; }
            catch { return -2; }
        }

        private static int SafeChannelCount(InteropDataHandler handler)
        {
            try { return handler == null || handler.m_channels == null ? -1 : handler.m_channels.Count; }
            catch { return -2; }
        }

        private static string SafeChannelName(InteropDataHandler handler, Bindings.Key key)
        {
            try
            {
                if (handler != null && handler.m_channels != null && handler.m_channels.ContainsKey(key.m_key))
                    return handler.m_channels[key.m_key] ?? "<null>";
            }
            catch { }
            return "<none>";
        }

        private static VisualElement FindSelectedShowPerson(StringBuilder sb)
        {
            try
            {
                var docs = Resources.FindObjectsOfTypeAll<UIDocument>();
                sb.AppendLine("UIDocument count=" + (docs == null ? 0 : docs.Length));
                if (docs == null) return null;
                for (int i = 0; i < docs.Length; i++)
                {
                    var doc = docs[i];
                    if (doc == null) continue;
                    VisualElement root = null;
                    try { root = doc.rootVisualElement; } catch { }
                    var found = FindSelectedRecursive(root);
                    if (found != null)
                    {
                        sb.AppendLine("selected element found in UIDocument[" + i + "]");
                        return found;
                    }
                }
            }
            catch (Exception ex) { sb.AppendLine("FindSelected FAIL: " + ex.GetType().Name + " - " + ex.Message); }
            return null;
        }

        private static VisualElement FindSelectedRecursive(VisualElement ve)
        {
            if (ve == null) return null;
            try
            {
                if (ve.ClassListContains("virtualised-list__item--selected"))
                {
                    var show = FindNamedRecursive(ve, "ShowPerson");
                    if (show != null) return show;
                }
            }
            catch { }
            int count = 0;
            try { count = ve.childCount; } catch { }
            for (int i = 0; i < count; i++)
            {
                VisualElement child = null;
                try { child = ve[i]; } catch { }
                var found = FindSelectedRecursive(child);
                if (found != null) return found;
            }
            return null;
        }

        private static VisualElement FindNamedRecursive(VisualElement ve, string name)
        {
            if (ve == null) return null;
            try { if (ve.name == name) return ve; } catch { }
            int count = 0;
            try { count = ve.childCount; } catch { }
            for (int i = 0; i < count; i++)
            {
                VisualElement child = null;
                try { child = ve[i]; } catch { }
                var found = FindNamedRecursive(child, name);
                if (found != null) return found;
            }
            return null;
        }

        private static unsafe Bindings.Key CreateTemporaryNode(BindingSubsystem bindings, string name)
        {
            fixed (char* p = name)
            {
                var span = new Il2CppSystem.ReadOnlySpan<char>((void*)p, name.Length);
                return bindings.Create(ref span, Bindings.NodeFlags.Temporary);
            }
        }

        private static InteropDataHandler FindLiveInteropHandler(StringBuilder sb, BindingSubsystem bindings)
        {
            try
            {
                var registry = bindings.m_handlers;
                if (registry == null) return null;
                sb.AppendLine("handler registry count=" + registry.Count);
                foreach (var pair in registry)
                {
                    string n = "";
                    try { n = pair.Key == null ? "" : pair.Key.FullName; } catch { }
                    if (n == null || !n.Contains("InteropReference")) continue;
                    var list = pair.Value;
                    if (list == null) continue;
                    for (int i = 0; i < list.Count; i++)
                    {
                        var h = list[i];
                        if (h == null) continue;
                        try
                        {
                            var concrete = new InteropDataHandler(h.Pointer);
                            if (concrete.m_interop != null)
                            {
                                sb.AppendLine("InteropDataHandler ptr=0x" + concrete.Pointer.ToString("X") + " interop=0x" + concrete.m_interop.Pointer.ToString("X"));
                                return concrete;
                            }
                        }
                        catch { }
                    }
                }
            }
            catch (Exception ex) { sb.AppendLine("Find handler FAIL: " + ex.GetType().Name + " - " + ex.Message); }
            return null;
        }

        private void CloseChannel()
        {
            if (!_channelOpen || _handler == null) return;
            try { _handler.CloseChannel(_key); }
            catch (Exception ex) { try { _sb?.AppendLine("CLOSE FAIL: " + ex.GetType().Name + " - " + ex.Message); } catch { } }
            _channelOpen = false;
        }

        private void SaveAndReset()
        {
            CloseChannel();
            _waiting = false;
            if (_sb != null) Save(_sb);
            _source = null;
            _handler = null;
            _bindings = null;
            _sb = null;
        }

        private static bool SafeValid(Bindings.Key key) { try { return key.IsValid(); } catch { return false; } }
        private static bool SafeExists(BindingSubsystem b, ref Bindings.Key k) { try { return b != null && b.Exists(ref k); } catch { return false; } }
        private static string SafeType(TypedValue tv) { try { return tv == null || tv.DataType == null ? "<null>" : tv.DataType.FullName; } catch { return "<failed>"; } }
        private static string SafeText(TypedValue tv) { try { return tv == null ? "<null>" : (tv.AsString() ?? ""); } catch (Exception ex) { return "<" + ex.GetType().Name + ">"; } }

        private static void Save(StringBuilder sb)
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Sports Interactive", "Football Manager 26", "FM26FullPlayerProbe");
                Directory.CreateDirectory(dir);
                string file = Path.Combine(dir, "handlertree_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".txt");
                File.WriteAllText(file, sb.ToString(), Encoding.UTF8);
                Plugin.Log.LogInfo("[FM26FullProbe] Saved: " + file);
            }
            catch (Exception ex) { Plugin.Log.LogError("[FM26FullProbe] Save failed: " + ex); }
        }
    }
}
