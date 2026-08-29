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
    [BepInPlugin("com.schumy12.fm26.fullplayerprobe", "FM26 Full Player Probe", "0.30.1")]
    public sealed class Plugin : BasePlugin
    {
        internal static new BepInEx.Logging.ManualLogSource Log;
        private static ProbeBehaviour _behaviour;

        public override void Load()
        {
            Log = base.Log;
            Log.LogInfo("[FM26FullProbe] Loaded v0.30.1 SELECTED UI CONTEXT REFERENCE - select one player in Recruitment > Players in Range and press F8.");
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
        private const uint UniqueIdProperty = 1970170212u;
        private const float WaitSeconds = 1.5f;

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
                    FinishBackendCheck();
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
            _sb.AppendLine("=== FM26 FULL PLAYER PROBE 0.30.1 SELECTED UI CONTEXT REFERENCE ===");
            _sb.AppendLine("0.29 proved synthetic PersonReference indices 0..31 open native channels but resolve no data.");
            _sb.AppendLine("This probe retrieves context-menu TypedValue objects from the actually selected ShowPerson element, then asks the live InteropDataHandler for UniqueId using that exact object.");
            _sb.AppendLine("No Harmony. No reflection getter invocation. No Bindings.Remove.");
            _sb.AppendLine();

            var showPerson = FindSelectedShowPerson(_sb);
            if (showPerson == null)
            {
                _sb.AppendLine("RESULT: selected ShowPerson element NOT FOUND");
                SaveAndReset();
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
                SaveAndReset();
                return;
            }

            if (objects == null || objects.Count == 0)
            {
                _sb.AppendLine("RESULT: no context objects returned");
                SaveAndReset();
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
                string text = SafeText(tv);
                _sb.AppendLine("OBJ[" + i + "] type=" + type + " text='" + text + "'");

                try
                {
                    var raw = tv.Get();
                    if (raw != null)
                        _sb.AppendLine("  raw ptr=0x" + raw.Pointer.ToString("X") + " il2cppType=" + (raw.GetIl2CppType() == null ? "<null>" : raw.GetIl2CppType().FullName));
                }
                catch (Exception ex)
                {
                    _sb.AppendLine("  raw Get failed: " + ex.GetType().Name + " - " + ex.Message);
                }

                if (_source == null && IsLikelyPlayerReference(type))
                {
                    _source = tv;
                    _sb.AppendLine("  SELECTED AS BACKEND SOURCE");
                }
            }

            if (_source == null)
            {
                _sb.AppendLine("RESULT: context objects exist, but no Person/Player reference TypedValue found");
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
                _sb.AppendLine("RESULT: live InteropDataHandler NOT FOUND");
                SaveAndReset();
                return;
            }

            try
            {
                _key = CreateTemporaryNode(_bindings, "__fm26probe_selected_context");
                _sb.AppendLine("NODE key=" + SafeKey(_key) + " raw=" + _key.m_key + " valid=" + SafeValid(_key) + " exists=" + SafeExists(_bindings, ref _key));

                var property = new Bindings.Property("UniqueId", new PropertyID(UniqueIdProperty));
                _handler.OpenChannel(_source, property, _key);
                _channelOpen = true;

                string channelName = "<none>";
                try
                {
                    if (_handler.m_channels != null && _handler.m_channels.ContainsKey(_key.m_key))
                        channelName = _handler.m_channels[_key.m_key] ?? "<null>";
                }
                catch { }

                _sb.AppendLine("OPEN selected source type=" + SafeType(_source) + " channel='" + channelName + "'");
                _sb.AppendLine("Waiting " + WaitSeconds + "s for native callback...");
                _waiting = true;
                _checkAt = Time.unscaledTime + WaitSeconds;
            }
            catch (Exception ex)
            {
                _sb.AppendLine("BACKEND OPEN FAIL: " + ex.GetType().Name + " - " + ex.Message);
                SaveAndReset();
            }
        }

        private void FinishBackendCheck()
        {
            _waiting = false;
            _sb.AppendLine();
            _sb.AppendLine("=== BACKEND RESULT ===");

            try
            {
                bool exists = _bindings != null && _bindings.Exists(ref _key);
                bool isSet = _bindings != null && _bindings.IsDataSet(_key);
                _sb.AppendLine("exists=" + exists + " isDataSet=" + isSet);

                if (exists && isSet)
                {
                    var tv = _bindings.Get(ref _key);
                    if (tv == null)
                        _sb.AppendLine("VALUE <null>");
                    else
                        _sb.AppendLine("VALUE type=" + SafeType(tv) + " text='" + SafeText(tv) + "'");
                }
                else
                {
                    _sb.AppendLine("VALUE not produced");
                }
            }
            catch (Exception ex)
            {
                _sb.AppendLine("BACKEND READ FAIL: " + ex.GetType().Name + " - " + ex.Message);
            }

            CloseChannel();
            SaveAndReset();
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

        private static bool IsLikelyPlayerReference(string type)
        {
            if (string.IsNullOrEmpty(type)) return false;
            if (!type.Contains("Reference")) return false;
            return type.Contains("Person") || type.Contains("Player");
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
                string file = Path.Combine(dir, "selectedcontext_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".txt");
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
