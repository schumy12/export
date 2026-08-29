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
    [BepInPlugin("com.schumy12.fm26.fullplayerprobe", "FM26 Full Player Probe", "0.34.1")]
    public sealed class Plugin : BasePlugin
    {
        internal static new BepInEx.Logging.ManualLogSource Log;
        private static ProbeBehaviour _behaviour;

        public override void Load()
        {
            Log = base.Log;
            Log.LogInfo("[FM26FullProbe] Loaded v0.34.1 ATTACHED DATA + DIRECT INTEROP - select one player and press F8.");
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
        private Bindings.Node _node;
        private Bindings.Data _data;
        private TypedValue _source;
        private StringBuilder _sb;
        private bool _waiting;
        private bool _channelOpen;
        private float _checkAt;

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
            _sb.AppendLine("=== FM26 FULL PLAYER PROBE 0.34.1 ATTACHED DATA + DIRECT INTEROP ===");
            _sb.AppendLine("0.33 proved the runtime has Node/Data/DataKey/OpenRequest plumbing, but the build-time wrapper does not expose TryOpenChannel.");
            _sb.AppendLine("This probe keeps the important part: allocate a real Bindings.Data entry, attach it to the node, then open the native InteropDataHandler channel on that attached node.");
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

                _source = null;
                for (int i = 0; i < objects.Count; i++)
                {
                    var tv = objects[i];
                    string type = SafeType(tv);
                    _sb.AppendLine("OBJ[" + i + "] type=" + type + " text='" + SafeText(tv) + "'");
                    if (_source == null && type == "FM.UI.PersonReference") _source = tv;
                }
                if (_source == null)
                {
                    _sb.AppendLine("RESULT: no PersonReference source");
                    SaveAndReset();
                    return;
                }

                var raw = _source.Get();
                var pr = new PersonReference(raw.Pointer);
                _sb.AppendLine("REAL PERSON Data1=" + pr.Data1 + " m_index=" + pr.m_index + " combined=" + pr.CombinedIndexAndType + " type=" + pr.Type);
            }
            catch (Exception ex)
            {
                _sb.AppendLine("context/source FAIL: " + ex.GetType().Name + " - " + ex.Message);
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

            try
            {
                _key = CreateTemporaryNode(_bindings, "__fm26probe_attached_data_name");
                _sb.AppendLine("NODE key=" + _key.m_key + " valid=" + _key.IsValid() + " exists=" + _bindings.Exists(ref _key));

                if (_bindings.m_nodes == null || !_bindings.m_nodes.ContainsKey(_key.m_key))
                {
                    _sb.AppendLine("RESULT: created key not present in m_nodes");
                    SaveAndReset();
                    return;
                }

                _node = _bindings.m_nodes[_key.m_key];
                if (_node == null)
                {
                    _sb.AppendLine("RESULT: m_nodes entry is null");
                    SaveAndReset();
                    return;
                }

                _sb.AppendLine("NODE before: name='" + (_node.m_name ?? "") + "' dataKeyRaw=" + _node.m_dataKey.m_key + " dataKeyValid=" + _node.m_dataKey.IsValid() + " propID=" + SafePropertyId(_node.m_propID));
                _node.m_propID = new PropertyID(NameProperty);

                _data = _bindings.GetNewData();
                if (_data == null)
                {
                    _sb.AppendLine("RESULT: GetNewData returned null");
                    SaveAndReset();
                    return;
                }

                _sb.AppendLine("DATA allocated: keyRaw=" + _data.key.m_key + " keyValid=" + _data.key.IsValid() + " isSet=" + _data.IsSet + " hasOpenChannel=" + _data.HasOpenChannel);
                _bindings.SetTargetData(_data, _node);

                var dataKey = _node.m_dataKey;
                _sb.AppendLine("NODE after SetTargetData: dataKeyRaw=" + dataKey.m_key + " dataKeyValid=" + dataKey.IsValid() + " propID=" + SafePropertyId(_node.m_propID));
                _sb.AppendLine("DATA after target: keyRaw=" + _data.key.m_key + " isSet=" + _data.IsSet + " hasOpenChannel=" + _data.HasOpenChannel + " opener=" + _data.opener.m_key);

                var property = new Bindings.Property("Name", new PropertyID(NameProperty));
                var contexts = new Il2CppSystem.Collections.Generic.List<string>();
                _sb.AppendLine("schemaAccepts=" + PersonReference.AcceptsPropertyInternal(NameProperty) + " canHandle=" + _handler.CanHandle(_source, property, contexts));
                _sb.AppendLine("handler channels before=" + SafeChannelCount(_handler));
                _sb.AppendLine("interop batchedRequests before=" + SafeBatchCount(_handler));

                _handler.OpenChannel(_source, property, _key);
                _channelOpen = true;

                _sb.AppendLine("channel='" + SafeChannelName(_handler, _key) + "'");
                _sb.AppendLine("handler channels immediatelyAfterOpen=" + SafeChannelCount(_handler));
                _sb.AppendLine("interop batchedRequests immediatelyAfterOpen=" + SafeBatchCount(_handler));
                _sb.AppendLine("DATA immediatelyAfterOpen: isSet=" + _data.IsSet + " hasOpenChannel=" + _data.HasOpenChannel + " handlerPtr=" + SafeHandlerPointer(_data));

                _waiting = true;
                _checkAt = Time.unscaledTime + WaitSeconds;
            }
            catch (Exception ex)
            {
                _sb.AppendLine("PLUMBING/OPEN FAIL: " + ex.GetType().Name + " - " + ex.Message);
                _sb.AppendLine(ex.ToString());
                SaveAndReset();
            }
        }

        private void FinishProbe()
        {
            _waiting = false;
            _sb.AppendLine();
            _sb.AppendLine("=== AFTER " + WaitSeconds + "s ===");

            try
            {
                _sb.AppendLine("handler channels before close=" + SafeChannelCount(_handler));
                _sb.AppendLine("interop batchedRequests=" + SafeBatchCount(_handler));

                if (_data != null)
                {
                    _sb.AppendLine("DATA: keyRaw=" + _data.key.m_key + " isSet=" + _data.IsSet + " hasOpenChannel=" + _data.HasOpenChannel + " handlerPtr=" + SafeHandlerPointer(_data) + " opener=" + _data.opener.m_key);
                    var dv = _data.Value;
                    _sb.AppendLine("DATA VALUE=" + (dv == null ? "<null>" : (SafeType(dv) + " '" + SafeText(dv) + "'")));
                }

                if (_node != null)
                {
                    var nk = _node.m_dataKey;
                    _sb.AppendLine("NODE: dataKeyRaw=" + nk.m_key + " dataKeyValid=" + nk.IsValid() + " propID=" + SafePropertyId(_node.m_propID));
                    var nv = _node.Value;
                    _sb.AppendLine("NODE VALUE=" + (nv == null ? "<null>" : (SafeType(nv) + " '" + SafeText(nv) + "'")));
                }

                bool exists = _bindings != null && _bindings.Exists(ref _key);
                bool set = _bindings != null && _bindings.IsDataSet(_key);
                _sb.AppendLine("GLOBAL exists=" + exists + " isDataSet=" + set);
                if (exists && set)
                {
                    var value = _bindings.Get(ref _key);
                    _sb.AppendLine("GLOBAL VALUE=" + (value == null ? "<null>" : (SafeType(value) + " '" + SafeText(value) + "'")));
                }
            }
            catch (Exception ex)
            {
                _sb.AppendLine("READBACK FAIL: " + ex.GetType().Name + " - " + ex.Message);
                _sb.AppendLine(ex.ToString());
            }

            CloseChannel();
            _sb.AppendLine("handler channels after close=" + SafeChannelCount(_handler));
            _sb.AppendLine("interop batchedRequests after close=" + SafeBatchCount(_handler));
            SaveAndReset();
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

        private static string SafeHandlerPointer(Bindings.Data data)
        {
            try
            {
                if (data == null || data.handler == null) return "<null>";
                return "0x" + data.handler.Pointer.ToString("X");
            }
            catch { return "<failed>"; }
        }

        private static string SafePropertyId(PropertyID id)
        {
            try { return id.ToString(); }
            catch { return "<failed>"; }
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
            _data = null;
            _node = null;
            _source = null;
            _handler = null;
            _bindings = null;
            _sb = null;
        }

        private static string SafeType(TypedValue tv)
        {
            try { return tv == null || tv.DataType == null ? "<null>" : tv.DataType.FullName; }
            catch { return "<failed>"; }
        }

        private static string SafeText(TypedValue tv)
        {
            try { return tv == null ? "<null>" : (tv.AsString() ?? ""); }
            catch (Exception ex) { return "<" + ex.GetType().Name + ">"; }
        }

        private static void Save(StringBuilder sb)
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Sports Interactive", "Football Manager 26", "FM26FullPlayerProbe");
                Directory.CreateDirectory(dir);
                string file = Path.Combine(dir, "attacheddata_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".txt");
                File.WriteAllText(file, sb.ToString(), Encoding.UTF8);
                Plugin.Log.LogInfo("[FM26FullProbe] Saved: " + file);
            }
            catch (Exception ex) { Plugin.Log.LogError("[FM26FullProbe] Save failed: " + ex); }
        }
    }
}
