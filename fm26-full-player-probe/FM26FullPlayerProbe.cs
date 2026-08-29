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
using FM.GamePlugin;
using SI.Core;
using SI.Bindable;
using SI.Bindable.Reference.Core;

namespace FM26FullPlayerProbe
{
    [BepInPlugin("com.schumy12.fm26.fullplayerprobe", "FM26 Full Player Probe", "0.40.0")]
    public sealed class Plugin : BasePlugin
    {
        internal static new BepInEx.Logging.ManualLogSource Log;
        private static ProbeBehaviour _behaviour;
        public override void Load()
        {
            Log = base.Log;
            Log.LogInfo("[FM26FullProbe] Loaded v0.40 MULTI TRUE DATA - select one player row and press F8 once.");
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
        private struct Target
        {
            public string Name;
            public uint Id;
            public Target(string name, uint id) { Name = name; Id = id; }
        }

        private static readonly Target[] Targets = new Target[]
        {
            new Target("PlayerCurrentAbility", 1346584898u),
            new Target("PlayerPotentialAbility", 1347436866u),
            new Target("AttributeProfessionalism", 1349546607u),
            new Target("Consistency", 1346588494u),
            new Target("Acceleration", 892805152u),
            new Target("Passing", 859381792u)
        };

        private const float WaitSeconds = 1.0f;
        private BindingSubsystem _bindings;
        private InteropDataHandler _handler;
        private IDataHandler _handlerInterface;
        private GameInteropSubsystem _interop;
        private Bindings.Key _key;
        private Bindings.Node _node;
        private Bindings.Data _data;
        private TypedValue _source;
        private StringBuilder _sb;
        private int _targetIndex;
        private bool _waiting;
        private bool _channelOpen;
        private bool _nativeNodeAdded;
        private float _checkAt;

        public ProbeBehaviour(IntPtr ptr) : base(ptr) { }

        private void Update()
        {
            try
            {
                if (!_waiting && _sb == null && Keyboard.current != null && Keyboard.current.f8Key.wasPressedThisFrame) StartProbe();
                if (_waiting && Time.unscaledTime >= _checkAt) FinishCurrentTarget();
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
            _sb.AppendLine("=== FM26 FULL PLAYER PROBE 0.40 MULTI TRUE DATA ===");
            _sb.AppendLine("Sequential test on one real selected PersonReference using the proven full native graph.");
            _sb.AppendLine("Targets: CA, PA, Professionalism, Consistency, Acceleration, Passing.");
            _sb.AppendLine();

            var showPerson = FindSelectedShowPerson(_sb);
            if (showPerson == null) { _sb.AppendLine("RESULT: selected ShowPerson NOT FOUND"); SaveAndReset(); return; }

            try
            {
                BindingPath path; ActionGroupings groupings; bool multiple;
                var objects = PluginContextMenuContributor.GetContextMenuObjects(showPerson, out path, out groupings, out multiple);
                _sb.AppendLine("GetContextMenuObjects count=" + (objects == null ? -1 : objects.Count) + " multiple=" + multiple);
                if (objects == null || objects.Count == 0) { _sb.AppendLine("RESULT: no context objects"); SaveAndReset(); return; }
                for (int i = 0; i < objects.Count; i++)
                {
                    var tv = objects[i];
                    string type = SafeType(tv);
                    _sb.AppendLine("OBJ[" + i + "] type=" + type + " text='" + SafeText(tv) + "'");
                    if (_source == null && type == "FM.UI.PersonReference") _source = tv;
                }
                if (_source == null) { _sb.AppendLine("RESULT: no PersonReference source"); SaveAndReset(); return; }
                var raw = _source.Get();
                var pr = new PersonReference(raw.Pointer);
                _sb.AppendLine("REAL PERSON Data1=" + pr.Data1 + " m_index=" + pr.m_index + " combined=" + pr.CombinedIndexAndType + " type=" + pr.Type);
            }
            catch (Exception ex)
            {
                _sb.AppendLine("context/source FAIL: " + ex.GetType().Name + " - " + ex.Message);
                SaveAndReset(); return;
            }

            _bindings = EmbeddedDataHandler.s_bindingSubsystem;
            if (_bindings == null) { _sb.AppendLine("RESULT: BindingSubsystem NOT FOUND"); SaveAndReset(); return; }
            _handler = FindLiveInteropHandler(_sb, _bindings);
            if (_handler == null || _handlerInterface == null || _interop == null) { _sb.AppendLine("RESULT: Interop handler/subsystem NOT FOUND"); SaveAndReset(); return; }

            try
            {
                _key = CreateTemporaryNode(_bindings, "__fm26probe_multi_true_data");
                _sb.AppendLine("NODE key=" + _key.m_key + " valid=" + _key.IsValid() + " exists=" + _bindings.Exists(ref _key));
                if (_bindings.m_nodes == null || !_bindings.m_nodes.ContainsKey(_key.m_key)) { _sb.AppendLine("RESULT: created key not present in m_nodes"); SaveAndReset(); return; }
                _node = _bindings.m_nodes[_key.m_key];
                if (_node == null) { _sb.AppendLine("RESULT: m_nodes entry is null"); SaveAndReset(); return; }
                _targetIndex = 0;
                StartCurrentTarget();
            }
            catch (Exception ex)
            {
                _sb.AppendLine("START FAIL: " + ex.GetType().Name + " - " + ex.Message);
                SaveAndReset();
            }
        }

        private void StartCurrentTarget()
        {
            if (_targetIndex >= Targets.Length)
            {
                _sb.AppendLine();
                _sb.AppendLine("RESULT: all targets completed");
                SaveAndReset();
                return;
            }

            var t = Targets[_targetIndex];
            _sb.AppendLine();
            _sb.AppendLine("--- [" + _targetIndex + "] " + t.Name + " id=" + t.Id + " ---");
            var propId = new PropertyID(t.Id);
            _node.m_propID = propId;
            _data = _bindings.GetNewData();
            if (_data == null) throw new Exception("GetNewData returned null");
            _bindings.SetTargetData(_data, _node);
            var dataKey = _node.m_dataKey;
            var parentKey = _node.m_parent == null ? default(Bindings.Key) : _node.m_parent.m_key;

            dynamic runtimeInterop = _interop;
            runtimeInterop.AddNode(_key, parentKey, propId);
            _nativeNodeAdded = true;
            _interop.AddData(dataKey);
            _interop.SetTarget(_key, dataKey);
            _data.handler = _handlerInterface;
            _data.opener = _key;

            var property = new Bindings.Property(t.Name, propId);
            var contexts = new Il2CppSystem.Collections.Generic.List<string>();
            _sb.AppendLine("dataKey=" + dataKey.m_key + " accepts=" + PersonReference.AcceptsPropertyInternal(t.Id) + " canHandle=" + _handler.CanHandle(_source, property, contexts) + " hasOpenChannel=" + _data.HasOpenChannel);
            _handler.OpenChannel(_source, property, _key);
            _channelOpen = true;
            _sb.AppendLine("channel='" + SafeChannelName(_handler, _key) + "' batchedAfterOpen=" + SafeBatchCount(_handler));
            _waiting = true;
            _checkAt = Time.unscaledTime + WaitSeconds;
        }

        private void FinishCurrentTarget()
        {
            _waiting = false;
            var t = Targets[_targetIndex];
            try
            {
                string valueType = _data == null ? "<null>" : SafeType(_data.Value);
                string valueText = _data == null ? "<null>" : SafeText(_data.Value);
                _sb.AppendLine("RESULT " + t.Name + ": isSet=" + (_data != null && _data.IsSet) + " type=" + valueType + " value='" + valueText + "' batched=" + SafeBatchCount(_handler));
            }
            catch (Exception ex)
            {
                _sb.AppendLine("READ FAIL " + t.Name + ": " + ex.GetType().Name + " - " + ex.Message);
            }

            CloseChannel();
            if (_nativeNodeAdded && _interop != null)
            {
                try { _interop.RemoveNode(_key); _sb.AppendLine("native RemoveNode OK"); }
                catch (Exception ex) { _sb.AppendLine("native RemoveNode FAIL: " + ex.GetType().Name + " - " + ex.Message); SaveAndReset(); return; }
                _nativeNodeAdded = false;
            }

            _data = null;
            _targetIndex++;
            try { StartCurrentTarget(); }
            catch (Exception ex)
            {
                _sb.AppendLine("NEXT TARGET FAIL: " + ex.GetType().Name + " - " + ex.Message);
                SaveAndReset();
            }
        }

        private InteropDataHandler FindLiveInteropHandler(StringBuilder sb, BindingSubsystem bindings)
        {
            try
            {
                var registry = bindings.m_handlers;
                if (registry == null) return null;
                sb.AppendLine("handler registry count=" + registry.Count);
                foreach (var pair in registry)
                {
                    string n = ""; try { n = pair.Key == null ? "" : pair.Key.FullName; } catch { }
                    if (n == null || !n.Contains("InteropReference")) continue;
                    var list = pair.Value; if (list == null) continue;
                    for (int i = 0; i < list.Count; i++)
                    {
                        var h = list[i]; if (h == null) continue;
                        try
                        {
                            var concrete = new InteropDataHandler(h.Pointer);
                            if (concrete.m_interop != null)
                            {
                                _handlerInterface = h;
                                _interop = new GameInteropSubsystem(concrete.m_interop.Pointer);
                                sb.AppendLine("InteropDataHandler ptr=0x" + concrete.Pointer.ToString("X") + " interop=0x" + _interop.Pointer.ToString("X") + " IDataHandler ptr=0x" + h.Pointer.ToString("X"));
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

        private static int SafeBatchCount(InteropDataHandler h) { try { return h == null || h.m_interop == null || h.m_interop.m_batchedRequests == null ? -1 : h.m_interop.m_batchedRequests.Count; } catch { return -2; } }
        private static string SafeChannelName(InteropDataHandler h, Bindings.Key k) { try { if (h != null && h.m_channels != null && h.m_channels.ContainsKey(k.m_key)) return h.m_channels[k.m_key] ?? "<null>"; } catch { } return "<none>"; }
        private static string SafeType(TypedValue tv) { try { return tv == null || tv.DataType == null ? "<null>" : tv.DataType.FullName; } catch { return "<failed>"; } }
        private static string SafeText(TypedValue tv) { try { return tv == null ? "<null>" : (tv.AsString() ?? ""); } catch (Exception ex) { return "<" + ex.GetType().Name + ">"; } }

        private static VisualElement FindSelectedShowPerson(StringBuilder sb)
        {
            try
            {
                var docs = Resources.FindObjectsOfTypeAll<UIDocument>();
                sb.AppendLine("UIDocument count=" + (docs == null ? 0 : docs.Length));
                if (docs == null) return null;
                for (int i = 0; i < docs.Length; i++)
                {
                    var doc = docs[i]; if (doc == null) continue;
                    VisualElement root = null; try { root = doc.rootVisualElement; } catch { }
                    var found = FindSelectedRecursive(root);
                    if (found != null) { sb.AppendLine("selected element found in UIDocument[" + i + "]"); return found; }
                }
            }
            catch (Exception ex) { sb.AppendLine("FindSelected FAIL: " + ex.GetType().Name + " - " + ex.Message); }
            return null;
        }
        private static VisualElement FindSelectedRecursive(VisualElement ve)
        {
            if (ve == null) return null;
            try { if (ve.ClassListContains("virtualised-list__item--selected")) { var show = FindNamedRecursive(ve, "ShowPerson"); if (show != null) return show; } } catch { }
            int count = 0; try { count = ve.childCount; } catch { }
            for (int i = 0; i < count; i++) { VisualElement child = null; try { child = ve[i]; } catch { } var found = FindSelectedRecursive(child); if (found != null) return found; }
            return null;
        }
        private static VisualElement FindNamedRecursive(VisualElement ve, string name)
        {
            if (ve == null) return null;
            try { if (ve.name == name) return ve; } catch { }
            int count = 0; try { count = ve.childCount; } catch { }
            for (int i = 0; i < count; i++) { VisualElement child = null; try { child = ve[i]; } catch { } var found = FindNamedRecursive(child, name); if (found != null) return found; }
            return null;
        }

        private static unsafe Bindings.Key CreateTemporaryNode(BindingSubsystem bindings, string name)
        {
            fixed (char* p = name) { var span = new Il2CppSystem.ReadOnlySpan<char>((void*)p, name.Length); return bindings.Create(ref span, Bindings.NodeFlags.Temporary); }
        }

        private void CloseChannel()
        {
            if (!_channelOpen || _handler == null) return;
            try { _handler.CloseChannel(_key); } catch (Exception ex) { try { _sb?.AppendLine("CLOSE FAIL: " + ex.GetType().Name + " - " + ex.Message); } catch { } }
            _channelOpen = false;
        }

        private void SaveAndReset()
        {
            CloseChannel();
            if (_nativeNodeAdded && _interop != null)
            {
                try { _interop.RemoveNode(_key); } catch { }
                _nativeNodeAdded = false;
            }
            _waiting = false;
            if (_sb != null) Save(_sb);
            _data = null; _node = null; _source = null; _handlerInterface = null; _handler = null; _interop = null; _bindings = null; _sb = null;
        }

        private static void Save(StringBuilder sb)
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Sports Interactive", "Football Manager 26", "FM26FullPlayerProbe");
                Directory.CreateDirectory(dir);
                string file = Path.Combine(dir, "multitrue_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".txt");
                File.WriteAllText(file, sb.ToString(), Encoding.UTF8);
                Plugin.Log.LogInfo("[FM26FullProbe] Saved: " + file);
            }
            catch (Exception ex) { Plugin.Log.LogError("[FM26FullProbe] Save failed: " + ex); }
        }
    }
}
