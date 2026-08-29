using System;
using System.IO;
using System.Text;
using System.Reflection;
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
    [BepInPlugin("com.schumy12.fm26.fullplayerprobe", "FM26 Full Player Probe", "0.47.0")]
    public sealed class Plugin : BasePlugin
    {
        internal static new BepInEx.Logging.ManualLogSource Log;
        private static ProbeBehaviour _behaviour;

        public override void Load()
        {
            Log = base.Log;
            Log.LogInfo("[FM26FullProbe] Loaded v0.47 FOOT DYNAMIC METADATA - select one player row and press F8 once.");
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
        private const uint FootednessId = 1244885353u;
        private const uint CompetentPositionsId = 1483174254u;
        private const float WaitSeconds = 0.60f;

        private BindingSubsystem _bindings;
        private InteropDataHandler _handler;
        private IDataHandler _handlerInterface;
        private GameInteropSubsystem _interop;
        private Bindings.Key _key;
        private Bindings.Node _node;
        private Bindings.Data _data;
        private TypedValue _source;
        private StringBuilder _log;
        private int _stage;
        private bool _waiting;
        private bool _channelOpen;
        private bool _nativeNodeAdded;
        private float _checkAt;

        public ProbeBehaviour(IntPtr ptr) : base(ptr) { }

        private void Update()
        {
            try
            {
                if (!_waiting && _log == null && Keyboard.current != null && Keyboard.current.f8Key.wasPressedThisFrame) StartProbe();
                if (_waiting && Time.unscaledTime >= _checkAt) FinishStage();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("[FM26FullProbe] Update error: " + ex);
                try { _log?.AppendLine("FATAL: " + ex); } catch { }
                SaveAndReset();
            }
        }

        private void StartProbe()
        {
            _log = new StringBuilder();
            _log.AppendLine("=== FM26 FULL PLAYER PROBE 0.47 FOOT DYNAMIC METADATA ===");
            _log.AppendLine("0.46 proved Footedness is DynamicReference but GetPropertyValue asks for missing key 1886680684. This probe dumps DynamicReference metadata only, without invoking arbitrary reflected getters.");
            _log.AppendLine();

            var showPerson = FindSelectedShowPerson(_log);
            if (showPerson == null) { _log.AppendLine("RESULT: selected ShowPerson NOT FOUND"); SaveAndReset(); return; }

            try
            {
                BindingPath path; ActionGroupings groupings; bool multiple;
                var objects = PluginContextMenuContributor.GetContextMenuObjects(showPerson, out path, out groupings, out multiple);
                _log.AppendLine("GetContextMenuObjects count=" + (objects == null ? -1 : objects.Count) + " multiple=" + multiple);
                if (objects == null || objects.Count == 0) { _log.AppendLine("RESULT: no context objects"); SaveAndReset(); return; }
                for (int i = 0; i < objects.Count; i++)
                {
                    var tv = objects[i];
                    if (_source == null && SafeType(tv) == "FM.UI.PersonReference") _source = tv;
                }
                if (_source == null) { _log.AppendLine("RESULT: no PersonReference source"); SaveAndReset(); return; }
                var raw = _source.Get();
                var pr = new PersonReference(raw.Pointer);
                _log.AppendLine("REAL PERSON Data1=" + pr.Data1 + " m_index=" + pr.m_index + " combined=" + pr.CombinedIndexAndType + " type=" + pr.Type);
            }
            catch (Exception ex)
            {
                _log.AppendLine("context/source FAIL: " + ex.GetType().Name + " - " + ex.Message);
                SaveAndReset(); return;
            }

            _bindings = EmbeddedDataHandler.s_bindingSubsystem;
            if (_bindings == null) { _log.AppendLine("RESULT: BindingSubsystem NOT FOUND"); SaveAndReset(); return; }
            _handler = FindLiveInteropHandler(_log, _bindings);
            if (_handler == null || _handlerInterface == null || _interop == null) { _log.AppendLine("RESULT: Interop handler/subsystem NOT FOUND"); SaveAndReset(); return; }

            try
            {
                _key = CreateTemporaryNode(_bindings, "__fm26probe_foot_dynamic_metadata");
                _log.AppendLine("NODE key=" + _key.m_key + " valid=" + _key.IsValid() + " exists=" + _bindings.Exists(ref _key));
                if (_bindings.m_nodes == null || !_bindings.m_nodes.ContainsKey(_key.m_key)) { _log.AppendLine("RESULT: node missing"); SaveAndReset(); return; }
                _node = _bindings.m_nodes[_key.m_key];
                _stage = 0;
                StartStage();
            }
            catch (Exception ex)
            {
                _log.AppendLine("START FAIL: " + ex.GetType().Name + " - " + ex.Message);
                SaveAndReset();
            }
        }

        private void StartStage()
        {
            if (_stage >= 2)
            {
                _log.AppendLine();
                _log.AppendLine("RESULT: dynamic metadata probe completed");
                SaveAndReset();
                return;
            }

            string name = _stage == 0 ? "Footedness" : "CompetentPositionsListLong";
            uint id = _stage == 0 ? FootednessId : CompetentPositionsId;
            var propId = new PropertyID(id);
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

            var property = new Bindings.Property(name, propId);
            var contexts = new Il2CppSystem.Collections.Generic.List<string>();
            bool canHandle = _handler.CanHandle(_source, property, contexts);
            _log.AppendLine();
            _log.AppendLine("STAGE " + _stage + " " + name + " canHandle=" + canHandle);
            _handler.OpenChannel(_source, property, _key);
            _channelOpen = true;
            _waiting = true;
            _checkAt = Time.unscaledTime + WaitSeconds;
        }

        private void FinishStage()
        {
            _waiting = false;
            TypedValue tv = null;
            bool isSet = false;
            try
            {
                isSet = _data != null && _data.IsSet;
                tv = _data == null ? null : _data.Value;
                _log.AppendLine("RAW isSet=" + isSet + " type=" + SafeType(tv) + " value='" + CleanUiString(SafeText(tv)) + "'");
            }
            catch (Exception ex) { _log.AppendLine("RAW READ FAIL: " + ex.GetType().Name + " - " + ex.Message); }

            if (isSet && tv != null)
            {
                if (_stage == 0) InspectFootedness(tv);
                else InspectCompetentPositions(tv);
            }

            CleanupGraph();
            _stage++;
            try { StartStage(); }
            catch (Exception ex) { _log.AppendLine("NEXT FAIL: " + ex.GetType().Name + " - " + ex.Message); SaveAndReset(); }
        }

        private void InspectFootedness(TypedValue tv)
        {
            if (SafeType(tv) != "SI.Bindable.DynamicReference")
            {
                _log.AppendLine("FOOT not DynamicReference");
                return;
            }

            try
            {
                var dyn = VisualFunctionLibrary.GetDynamicReference(tv);
                _log.AppendLine("FOOT dyn ptr=0x" + dyn.Pointer.ToString("X"));
                DumpTypeMetadata(typeof(DynamicReference), "DynamicReference");
            }
            catch (Exception ex) { _log.AppendLine("FOOT GetDynamicReference FAIL: " + ex.GetType().Name + " - " + ex.Message); }
        }

        private void InspectCompetentPositions(TypedValue tv)
        {
            try
            {
                var list = tv.Get<Il2CppSystem.Collections.Generic.List<TypedValue>>();
                if (list == null) { _log.AppendLine("POSITION LIST null"); return; }
                _log.AppendLine("POSITION LIST count=" + list.Count);
                for (int i = 0; i < list.Count; i++)
                {
                    var item = list[i];
                    _log.AppendLine("POSITION ITEM[" + i + "] type=" + SafeType(item) + " raw='" + CleanUiString(SafeText(item)) + "'");
                    if (SafeType(item) == "SI.Bindable.DynamicReference")
                    {
                        try
                        {
                            var dyn = VisualFunctionLibrary.GetDynamicReference(item);
                            var inner = VisualFunctionLibrary.GetPropertyValue(dyn);
                            _log.AppendLine("POSITION ITEM[" + i + "] valueType=" + SafeType(inner) + " value='" + CleanUiString(SafeText(inner)) + "'");
                        }
                        catch (Exception ex) { _log.AppendLine("POSITION ITEM[" + i + "] FAIL: " + ex.GetType().Name + " - " + ex.Message); }
                    }
                }
            }
            catch (Exception ex) { _log.AppendLine("POSITION LIST READ FAIL: " + ex.GetType().Name + " - " + ex.Message); }
        }

        private void DumpTypeMetadata(Type type, string label)
        {
            try
            {
                _log.AppendLine("--- " + label + " metadata ---");
                foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
                    _log.AppendLine("PROP " + (p.GetMethod != null && p.GetMethod.IsStatic ? "static " : "") + p.PropertyType.FullName + " " + p.Name);
                foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
                    _log.AppendLine("FIELD " + (f.IsStatic ? "static " : "") + f.FieldType.FullName + " " + f.Name);
                foreach (var m in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                {
                    var ps = m.GetParameters();
                    var sb = new StringBuilder();
                    for (int i = 0; i < ps.Length; i++) { if (i > 0) sb.Append(", "); sb.Append(ps[i].ParameterType.FullName); }
                    _log.AppendLine("METHOD " + (m.IsStatic ? "static " : "") + m.ReturnType.FullName + " " + m.Name + "(" + sb + ")");
                }
            }
            catch (Exception ex) { _log.AppendLine(label + " metadata FAIL: " + ex.GetType().Name + " - " + ex.Message); }
        }

        private InteropDataHandler FindLiveInteropHandler(StringBuilder sb, BindingSubsystem bindings)
        {
            try
            {
                var registry = bindings.m_handlers;
                if (registry == null) return null;
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
                                sb.AppendLine("InteropDataHandler ptr=0x" + concrete.Pointer.ToString("X") + " interop=0x" + _interop.Pointer.ToString("X"));
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

        private static string SafeType(TypedValue tv) { try { return tv == null || tv.DataType == null ? "<null>" : tv.DataType.FullName; } catch { return "<failed>"; } }
        private static string SafeText(TypedValue tv) { try { return tv == null ? "" : (tv.AsString() ?? ""); } catch { return ""; } }

        private static string CleanUiString(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            int marker = s.LastIndexOf('\u0002');
            if (marker >= 0 && marker + 1 < s.Length) s = s.Substring(marker + 1);
            var sb = new StringBuilder();
            for (int i = 0; i < s.Length; i++) if (!char.IsControl(s[i])) sb.Append(s[i]);
            return sb.ToString().Trim();
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
            try { _handler.CloseChannel(_key); } catch (Exception ex) { try { _log?.AppendLine("CLOSE FAIL: " + ex.GetType().Name + " - " + ex.Message); } catch { } }
            _channelOpen = false;
        }

        private void CleanupGraph()
        {
            CloseChannel();
            if (_nativeNodeAdded && _interop != null)
            {
                try { _interop.RemoveNode(_key); } catch (Exception ex) { try { _log?.AppendLine("RemoveNode FAIL: " + ex.GetType().Name + " - " + ex.Message); } catch { } }
                _nativeNodeAdded = false;
            }
            _data = null;
        }

        private void SaveAndReset()
        {
            CleanupGraph();
            _waiting = false;
            if (_log != null)
            {
                try
                {
                    string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Sports Interactive", "Football Manager 26", "FM26FullPlayerProbe");
                    Directory.CreateDirectory(dir);
                    string file = Path.Combine(dir, "edge47_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".txt");
                    File.WriteAllText(file, _log.ToString(), Encoding.UTF8);
                    Plugin.Log.LogInfo("[FM26FullProbe] Saved: " + file);
                }
                catch (Exception ex) { Plugin.Log.LogError("[FM26FullProbe] Save failed: " + ex); }
            }
            _data = null; _node = null; _source = null; _handlerInterface = null; _handler = null; _interop = null; _bindings = null; _log = null;
        }
    }
}
