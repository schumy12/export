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
    [BepInPlugin("com.schumy12.fm26.fullplayerprobe", "FM26 Full Player Probe", "0.31.0")]
    public sealed class Plugin : BasePlugin
    {
        internal static new BepInEx.Logging.ManualLogSource Log;
        private static ProbeBehaviour _behaviour;

        public override void Load()
        {
            Log = base.Log;
            Log.LogInfo("[FM26FullProbe] Loaded v0.31 REAL SELECTED PLAYER MULTI-PROPERTY - select one player and press F8.");
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
        private const float WaitPerProperty = 1.0f;

        private struct PropertySpec
        {
            public string Name;
            public uint Id;
            public PropertySpec(string name, uint id) { Name = name; Id = id; }
        }

        private static readonly PropertySpec[] Properties = new PropertySpec[]
        {
            new PropertySpec("UniqueId", 1970170212u),
            new PropertySpec("Name", 1851878757u),
            new PropertySpec("Surname", 843789105u),
            new PropertySpec("Age", 825565216u),
            new PropertySpec("IsPlayer", 862938733u),
            new PropertySpec("Club", 825630752u),
            new PropertySpec("Team", 1415930221u),
            new PropertySpec("Reputation", 1848658298u),
            new PropertySpec("PlayerCurrentAbility", 1346584898u),
            new PropertySpec("PlayerPotentialAbility", 1347436866u),
            new PropertySpec("AttributeAcceleration", 892805152u),
            new PropertySpec("Personality", 1349742196u)
        };

        private BindingSubsystem _bindings;
        private InteropDataHandler _handler;
        private Bindings.Key _key;
        private TypedValue _source;
        private StringBuilder _sb;
        private bool _running;
        private bool _channelOpen;
        private int _propertyIndex;
        private float _checkAt;

        public ProbeBehaviour(IntPtr ptr) : base(ptr) { }

        private void Update()
        {
            try
            {
                if (!_running && Keyboard.current != null && Keyboard.current.f8Key.wasPressedThisFrame)
                    StartProbe();

                if (_running && Time.unscaledTime >= _checkAt)
                    CheckCurrentProperty();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("[FM26FullProbe] Update error: " + ex);
                try { _sb?.AppendLine("UPDATE/FATAL: " + ex); } catch { }
                Finish("fatal update error");
            }
        }

        private void StartProbe()
        {
            _sb = new StringBuilder();
            _sb.AppendLine("=== FM26 FULL PLAYER PROBE 0.31 REAL SELECTED PLAYER MULTI-PROPERTY ===");
            _sb.AppendLine("0.30.1 successfully recovered the real selected FM.UI.PersonReference, but UniqueId alone produced no backend value.");
            _sb.AppendLine("This probe keeps that exact live PersonReference and asks several accepted properties sequentially.");
            _sb.AppendLine("It also logs the real PersonReference identity and InteropDataHandler.CanHandle result for every property.");
            _sb.AppendLine("No Harmony. No reflection getter invocation. No Bindings.Remove.");
            _sb.AppendLine();

            var showPerson = FindSelectedShowPerson(_sb);
            if (showPerson == null)
            {
                _sb.AppendLine("RESULT: selected ShowPerson element NOT FOUND");
                Finish("no selected ShowPerson");
                return;
            }

            _sb.AppendLine("Selected ShowPerson found name='" + SafeName(showPerson) + "'");

            Il2CppSystem.Collections.Generic.List<TypedValue> objects = null;
            try
            {
                BindingPath bindingPath;
                ActionGroupings actionGroupings;
                bool multiple;
                objects = PluginContextMenuContributor.GetContextMenuObjects(showPerson, out bindingPath, out actionGroupings, out multiple);
                _sb.AppendLine("GetContextMenuObjects returned count=" + (objects == null ? -1 : objects.Count) + " multiple=" + multiple);
            }
            catch (Exception ex)
            {
                _sb.AppendLine("GetContextMenuObjects FAIL: " + ex.GetType().Name + " - " + ex.Message);
                Finish("context lookup failed");
                return;
            }

            if (objects == null || objects.Count == 0)
            {
                _sb.AppendLine("RESULT: no context objects returned");
                Finish("no context objects");
                return;
            }

            _source = null;
            for (int i = 0; i < objects.Count; i++)
            {
                var tv = objects[i];
                if (tv == null)
                {
                    _sb.AppendLine("OBJ[" + i + "] <null>");
                    continue;
                }

                string type = SafeType(tv);
                _sb.AppendLine("OBJ[" + i + "] type=" + type + " text='" + SafeText(tv) + "'");

                try
                {
                    var raw = tv.Get();
                    if (raw != null)
                    {
                        _sb.AppendLine("  raw ptr=0x" + raw.Pointer.ToString("X") + " il2cppType=" + (raw.GetIl2CppType() == null ? "<null>" : raw.GetIl2CppType().FullName));
                        if (type == "FM.UI.PersonReference")
                        {
                            try
                            {
                                var pr = new PersonReference(raw.Pointer);
                                _sb.AppendLine("  REAL PERSON Data1=" + pr.Data1 + " m_index=" + pr.m_index + " combined=" + pr.CombinedIndexAndType + " type=" + pr.Type);
                            }
                            catch (Exception ex)
                            {
                                _sb.AppendLine("  REAL PERSON identity read failed: " + ex.GetType().Name + " - " + ex.Message);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _sb.AppendLine("  raw Get failed: " + ex.GetType().Name + " - " + ex.Message);
                }

                if (_source == null && type == "FM.UI.PersonReference")
                {
                    _source = tv;
                    _sb.AppendLine("  SELECTED AS BACKEND SOURCE");
                }
            }

            if (_source == null)
            {
                _sb.AppendLine("RESULT: no real PersonReference TypedValue found");
                Finish("no PersonReference");
                return;
            }

            _bindings = EmbeddedDataHandler.s_bindingSubsystem;
            if (_bindings == null)
            {
                _sb.AppendLine("RESULT: BindingSubsystem NOT FOUND");
                Finish("no BindingSubsystem");
                return;
            }

            _handler = FindLiveInteropHandler(_sb, _bindings);
            if (_handler == null)
            {
                _sb.AppendLine("RESULT: live InteropDataHandler NOT FOUND");
                Finish("no InteropDataHandler");
                return;
            }

            try
            {
                _key = CreateTemporaryNode(_bindings, "__fm26probe_real_player_props");
                _sb.AppendLine("NODE key=" + SafeKey(_key) + " raw=" + _key.m_key + " valid=" + SafeValid(_key) + " exists=" + SafeExists(_bindings, ref _key));
            }
            catch (Exception ex)
            {
                _sb.AppendLine("NODE CREATE FAIL: " + ex.GetType().Name + " - " + ex.Message);
                Finish("node create failed");
                return;
            }

            _propertyIndex = 0;
            _running = true;
            _channelOpen = false;
            OpenCurrentProperty();
        }

        private void OpenCurrentProperty()
        {
            if (!_running) return;
            if (_propertyIndex >= Properties.Length)
            {
                Finish("all properties completed without data");
                return;
            }

            var spec = Properties[_propertyIndex];
            var property = new Bindings.Property(spec.Name, new PropertyID(spec.Id));

            bool accepted = false;
            try { accepted = PersonReference.AcceptsPropertyInternal(spec.Id); }
            catch (Exception ex) { _sb.AppendLine("ACCEPTS FAIL " + spec.Name + ": " + ex.GetType().Name + " - " + ex.Message); }

            string canHandleText = "<not tested>";
            try
            {
                var contexts = new Il2CppSystem.Collections.Generic.List<string>();
                bool canHandle = _handler.CanHandle(_source, property, contexts);
                canHandleText = canHandle.ToString();
            }
            catch (Exception ex)
            {
                canHandleText = "<" + ex.GetType().Name + ": " + ex.Message + ">";
            }

            try
            {
                _handler.OpenChannel(_source, property, _key);
                _channelOpen = true;

                string channelName = "<none>";
                try
                {
                    if (_handler.m_channels != null && _handler.m_channels.ContainsKey(_key.m_key))
                        channelName = _handler.m_channels[_key.m_key] ?? "<null>";
                }
                catch { }

                _sb.AppendLine("OPEN prop[" + _propertyIndex + "] " + spec.Name + " id=" + spec.Id + " schemaAccepts=" + accepted + " canHandle=" + canHandleText + " channel='" + channelName + "'");
                _checkAt = Time.unscaledTime + WaitPerProperty;
            }
            catch (Exception ex)
            {
                _sb.AppendLine("OPEN FAIL prop[" + _propertyIndex + "] " + spec.Name + " " + ex.GetType().Name + " - " + ex.Message);
                CloseChannel();
                _propertyIndex++;
                OpenCurrentProperty();
            }
        }

        private void CheckCurrentProperty()
        {
            if (!_running) return;
            var spec = Properties[_propertyIndex];

            bool exists = false;
            bool isSet = false;
            try { exists = _bindings != null && _bindings.Exists(ref _key); }
            catch (Exception ex) { _sb.AppendLine("EXISTS FAIL " + spec.Name + ": " + ex.GetType().Name + " - " + ex.Message); }
            try { isSet = _bindings != null && _bindings.IsDataSet(_key); }
            catch (Exception ex) { _sb.AppendLine("ISDATASET FAIL " + spec.Name + ": " + ex.GetType().Name + " - " + ex.Message); }

            if (exists && isSet)
            {
                try
                {
                    var tv = _bindings.Get(ref _key);
                    if (tv == null)
                        _sb.AppendLine("VALUE " + spec.Name + " <null>");
                    else
                        _sb.AppendLine("VALUE " + spec.Name + " type=" + SafeType(tv) + " text='" + SafeText(tv) + "'");
                }
                catch (Exception ex)
                {
                    _sb.AppendLine("VALUE READ FAIL " + spec.Name + ": " + ex.GetType().Name + " - " + ex.Message);
                }

                Finish("backend produced data for " + spec.Name);
                return;
            }

            _sb.AppendLine("CHECK " + spec.Name + " exists=" + exists + " isDataSet=" + isSet);
            CloseChannel();
            _propertyIndex++;
            OpenCurrentProperty();
        }

        private void CloseChannel()
        {
            if (!_channelOpen || _handler == null) return;
            try { _handler.CloseChannel(_key); }
            catch (Exception ex) { try { _sb?.AppendLine("CLOSE FAIL propIndex=" + _propertyIndex + " " + ex.GetType().Name + " - " + ex.Message); } catch { } }
            _channelOpen = false;
        }

        private void Finish(string reason)
        {
            CloseChannel();
            _running = false;
            if (_sb == null) return;

            _sb.AppendLine();
            _sb.AppendLine("=== RESULT ===");
            _sb.AppendLine("reason=" + reason);
            _sb.AppendLine("lastPropertyIndex=" + _propertyIndex);
            try { _sb.AppendLine("node exists=" + (_bindings != null && _bindings.Exists(ref _key))); } catch { }
            try { _sb.AppendLine("handler channels after close=" + (_handler == null || _handler.m_channels == null ? -1 : _handler.m_channels.Count)); } catch { }
            _sb.AppendLine("NOTE: temporary node intentionally not removed because Bindings.Remove was unsafe in v0.28.1.");

            Save(_sb);
            _source = null;
            _handler = null;
            _bindings = null;
            _sb = null;
        }

        private static VisualElement FindSelectedShowPerson(StringBuilder sb)
        {
            try
            {
                var docs = Resources.FindObjectsOfTypeAll<UIDocument>();
                sb.AppendLine("UIDocument count=" + (docs == null ? 0 : docs.Length));
                if (docs == null) return null;

                for (int d = 0; d < docs.Length; d++)
                {
                    var doc = docs[d];
                    if (doc == null) continue;
                    VisualElement root = null;
                    try { root = doc.rootVisualElement; } catch { }
                    if (root == null) continue;

                    var found = FindSelectedShowPersonRecursive(root);
                    if (found != null)
                    {
                        sb.AppendLine("selected element found in UIDocument[" + d + "] name='" + SafeObjectName(doc) + "'");
                        return found;
                    }
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine("FindSelectedShowPerson FAIL: " + ex.GetType().Name + " - " + ex.Message);
            }
            return null;
        }

        private static VisualElement FindSelectedShowPersonRecursive(VisualElement element)
        {
            if (element == null) return null;

            bool selected = false;
            try { selected = element.ClassListContains("virtualised-list__item--selected"); } catch { }
            if (selected)
            {
                var show = FindNamedRecursive(element, "ShowPerson");
                if (show != null) return show;
            }

            int count = 0;
            try { count = element.childCount; } catch { }
            for (int i = 0; i < count; i++)
            {
                VisualElement child = null;
                try { child = element[i]; } catch { }
                var found = FindSelectedShowPersonRecursive(child);
                if (found != null) return found;
            }
            return null;
        }

        private static VisualElement FindNamedRecursive(VisualElement element, string target)
        {
            if (element == null) return null;
            try { if (element.name == target) return element; } catch { }

            int count = 0;
            try { count = element.childCount; } catch { }
            for (int i = 0; i < count; i++)
            {
                VisualElement child = null;
                try { child = element[i]; } catch { }
                var found = FindNamedRecursive(child, target);
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
                if (registry == null)
                {
                    sb.AppendLine("bindings.m_handlers=<null>");
                    return null;
                }

                sb.AppendLine("handler registry count=" + registry.Count);
                foreach (var pair in registry)
                {
                    string keyName = "";
                    try { keyName = pair.Key == null ? "<null>" : pair.Key.FullName; }
                    catch { keyName = "<type-name-failed>"; }
                    if (keyName == null || !keyName.Contains("InteropReference")) continue;

                    var list = pair.Value;
                    if (list == null) continue;
                    sb.AppendLine("candidate registry key='" + keyName + "' list=" + list.Count);

                    for (int i = 0; i < list.Count; i++)
                    {
                        var baseHandler = list[i];
                        if (baseHandler == null) continue;
                        try
                        {
                            var concrete = new InteropDataHandler(baseHandler.Pointer);
                            if (concrete.m_interop != null)
                            {
                                sb.AppendLine("InteropDataHandler ptr=0x" + concrete.Pointer.ToString("X") + " interop=0x" + concrete.m_interop.Pointer.ToString("X"));
                                return concrete;
                            }
                        }
                        catch (Exception ex)
                        {
                            sb.AppendLine("handler wrap failed: " + ex.GetType().Name + " - " + ex.Message);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine("FindLiveInteropHandler failed: " + ex.GetType().Name + " - " + ex.Message);
            }
            return null;
        }

        private static bool SafeValid(Bindings.Key key)
        {
            try { return key.IsValid(); } catch { return false; }
        }

        private static bool SafeExists(BindingSubsystem bindings, ref Bindings.Key key)
        {
            try { return bindings != null && bindings.Exists(ref key); } catch { return false; }
        }

        private static string SafeKey(Bindings.Key key)
        {
            try { return key.ToString() ?? "<null>"; }
            catch (Exception ex) { return "<" + ex.GetType().Name + ">"; }
        }

        private static string SafeType(TypedValue tv)
        {
            try { return tv.DataType == null ? "<null>" : tv.DataType.FullName; }
            catch (Exception ex) { return "<" + ex.GetType().Name + ">"; }
        }

        private static string SafeText(TypedValue tv)
        {
            try { return tv.AsString() ?? ""; }
            catch (Exception ex) { return "<" + ex.GetType().Name + ": " + ex.Message + ">"; }
        }

        private static string SafeName(VisualElement ve)
        {
            try { return ve == null ? "<null>" : (ve.name ?? ""); }
            catch { return "<failed>"; }
        }

        private static string SafeObjectName(UnityEngine.Object obj)
        {
            try { return obj == null ? "<null>" : (obj.name ?? ""); }
            catch { return "<failed>"; }
        }

        private static void Save(StringBuilder sb)
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Sports Interactive", "Football Manager 26", "FM26FullPlayerProbe");
                Directory.CreateDirectory(dir);
                string file = Path.Combine(dir, "realplayerprops_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".txt");
                File.WriteAllText(file, sb.ToString(), Encoding.UTF8);
                Plugin.Log.LogInfo("[FM26FullProbe] Saved: " + file);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("[FM26FullProbe] Save failed: " + ex);
            }
        }
    }
}
