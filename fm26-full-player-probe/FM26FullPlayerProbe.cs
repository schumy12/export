using System;
using System.IO;
using System.Text;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using UnityEngine;
using UnityEngine.InputSystem;
using FM.UI;
using SI.Core;
using SI.Bindable;
using SI.Bindable.Reference.Core;

namespace FM26FullPlayerProbe
{
    [BepInPlugin("com.schumy12.fm26.fullplayerprobe", "FM26 Full Player Probe", "0.29.0")]
    public sealed class Plugin : BasePlugin
    {
        internal static new BepInEx.Logging.ManualLogSource Log;
        private static ProbeBehaviour _behaviour;

        public override void Load()
        {
            Log = base.Log;
            Log.LogInfo("[FM26FullProbe] Loaded v0.29 SEQUENTIAL SINGLE NODE - press F8 after loading a save.");
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
        private const int ProbeCount = 32;
        private const float WaitPerIndex = 0.75f;

        private InteropDataHandler _handler;
        private BindingSubsystem _bindings;
        private Bindings.Key _key;
        private Bindings.Property _property;
        private TypedValue _currentSource;
        private StringBuilder _sb;
        private bool _running;
        private int _index;
        private float _nextCheckAt;
        private bool _channelOpen;

        public ProbeBehaviour(IntPtr ptr) : base(ptr) { }

        private void Update()
        {
            try
            {
                if (!_running && Keyboard.current != null && Keyboard.current.f8Key.wasPressedThisFrame)
                    StartProbe();

                if (_running && Time.unscaledTime >= _nextCheckAt)
                    CheckCurrentAndAdvance();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("[FM26FullProbe] Update error: " + ex);
                try { _sb?.AppendLine("UPDATE/FATAL: " + ex); } catch { }
                FinishProbe("fatal update error");
            }
        }

        private void StartProbe()
        {
            _sb = new StringBuilder();
            _sb.AppendLine("=== FM26 FULL PLAYER PROBE 0.29 SEQUENTIAL SINGLE NODE ===");
            _sb.AppendLine("0.28.1 proved Bindings.Create returns a valid registered node, but all managed-pointer span names collapsed to the same key.");
            _sb.AppendLine("This probe creates exactly one node and reuses it sequentially, one PersonReference at a time.");
            _sb.AppendLine("No Bindings.Remove call: 0.28.1 showed Remove is unsafe for this ad-hoc node.");
            _sb.AppendLine("Property UniqueId=" + UniqueIdProperty + ", indices=0.." + (ProbeCount - 1) + ", wait/index=" + WaitPerIndex + "s");
            _sb.AppendLine();

            _bindings = EmbeddedDataHandler.s_bindingSubsystem;
            if (_bindings == null)
            {
                _sb.AppendLine("RESULT: BindingSubsystem NOT FOUND");
                Save(_sb);
                return;
            }
            _sb.AppendLine("BindingSubsystem ptr=0x" + _bindings.Pointer.ToString("X"));

            _handler = FindLiveInteropHandler(_sb, _bindings);
            if (_handler == null)
            {
                _sb.AppendLine("RESULT: live InteropDataHandler NOT FOUND");
                Save(_sb);
                return;
            }

            try
            {
                _sb.AppendLine("handler ptr=0x" + _handler.Pointer.ToString("X"));
                _sb.AppendLine("handler channels before=" + (_handler.m_channels == null ? -1 : _handler.m_channels.Count));
            }
            catch (Exception ex)
            {
                _sb.AppendLine("handler state read failed: " + ex.GetType().Name + " - " + ex.Message);
            }

            try
            {
                _key = CreateTemporaryNode(_bindings, "__fm26probe_sequential");
                bool valid = false;
                bool exists = false;
                try { valid = _key.IsValid(); } catch { }
                try { exists = _bindings.Exists(ref _key); } catch { }
                _sb.AppendLine("NODE key=" + SafeKey(_key) + " raw=" + _key.m_key + " valid=" + valid + " exists=" + exists);
            }
            catch (Exception ex)
            {
                _sb.AppendLine("NODE CREATE FAIL: " + ex.GetType().Name + " - " + ex.Message);
                Save(_sb);
                return;
            }

            _property = new Bindings.Property("UniqueId", new PropertyID(UniqueIdProperty));
            _index = 0;
            _running = true;
            _channelOpen = false;
            OpenCurrent();
        }

        private void OpenCurrent()
        {
            if (!_running) return;
            if (_index >= ProbeCount)
            {
                FinishProbe("completed all indices without a value");
                return;
            }

            try
            {
                var pr = new PersonReference(_index);
                _currentSource = TypedValue.GetReferenceTypedValue();
                _currentSource.SetValue(pr);

                _handler.OpenChannel(_currentSource, _property, _key);
                _channelOpen = true;

                string channelName = "<none>";
                try
                {
                    if (_handler.m_channels != null && _handler.m_channels.ContainsKey(_key.m_key))
                        channelName = _handler.m_channels[_key.m_key] ?? "<null>";
                }
                catch (Exception ex)
                {
                    channelName = "<" + ex.GetType().Name + ">";
                }

                _sb.AppendLine("OPEN index=" + _index + " key=" + SafeKey(_key) + " data1=" + pr.Data1 + " channel='" + channelName + "'");
                _nextCheckAt = Time.unscaledTime + WaitPerIndex;
            }
            catch (Exception ex)
            {
                _sb.AppendLine("OPEN FAIL index=" + _index + " " + ex.GetType().Name + " - " + ex.Message);
                CloseCurrent();
                _index++;
                OpenCurrent();
            }
        }

        private void CheckCurrentAndAdvance()
        {
            if (!_running) return;

            bool exists = false;
            bool isSet = false;
            try { exists = _bindings.Exists(ref _key); }
            catch (Exception ex) { _sb.AppendLine("EXISTS FAIL index=" + _index + " " + ex.GetType().Name + " - " + ex.Message); }
            try { isSet = _bindings.IsDataSet(_key); }
            catch (Exception ex) { _sb.AppendLine("ISDATASET FAIL index=" + _index + " " + ex.GetType().Name + " - " + ex.Message); }

            if (exists && isSet)
            {
                try
                {
                    var tv = _bindings.Get(ref _key);
                    if (tv == null)
                        _sb.AppendLine("VALUE index=" + _index + " exists=true isDataSet=true <null>");
                    else
                        _sb.AppendLine("VALUE index=" + _index + " exists=true isDataSet=true type=" + SafeType(tv) + " text='" + SafeText(tv) + "'");
                }
                catch (Exception ex)
                {
                    _sb.AppendLine("VALUE READ FAIL index=" + _index + " " + ex.GetType().Name + " - " + ex.Message);
                }

                FinishProbe("backend produced data");
                return;
            }

            _sb.AppendLine("CHECK index=" + _index + " exists=" + exists + " isDataSet=" + isSet);
            CloseCurrent();
            _currentSource = null;
            _index++;
            OpenCurrent();
        }

        private void CloseCurrent()
        {
            if (!_channelOpen || _handler == null) return;
            try { _handler.CloseChannel(_key); }
            catch (Exception ex) { _sb?.AppendLine("CLOSE FAIL index=" + _index + " " + ex.GetType().Name + " - " + ex.Message); }
            _channelOpen = false;
        }

        private void FinishProbe(string reason)
        {
            if (_sb == null) return;

            CloseCurrent();
            _running = false;
            _sb.AppendLine();
            _sb.AppendLine("=== RESULT ===");
            _sb.AppendLine("reason=" + reason);
            _sb.AppendLine("lastIndex=" + _index);
            try { _sb.AppendLine("node exists=" + (_bindings != null && _bindings.Exists(ref _key))); } catch { }
            try { _sb.AppendLine("handler channels after close=" + (_handler == null || _handler.m_channels == null ? -1 : _handler.m_channels.Count)); } catch { }
            _sb.AppendLine("NOTE: node intentionally not removed because Bindings.Remove threw inside MarkChildrenDirtyRecursive in v0.28.1.");

            Save(_sb);
            _currentSource = null;
            _handler = null;
            _bindings = null;
            _sb = null;
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

                    sb.AppendLine("candidate registry key='" + keyName + "'");
                    var list = pair.Value;
                    if (list == null) continue;
                    sb.AppendLine("  handler list count=" + list.Count);

                    for (int i = 0; i < list.Count; i++)
                    {
                        var baseHandler = list[i];
                        if (baseHandler == null) continue;
                        try
                        {
                            var concrete = new InteropDataHandler(baseHandler.Pointer);
                            if (concrete.m_interop != null)
                            {
                                sb.AppendLine("  InteropDataHandler confirmed ptr=0x" + concrete.Pointer.ToString("X") + " interop=0x" + concrete.m_interop.Pointer.ToString("X"));
                                return concrete;
                            }
                        }
                        catch (Exception ex)
                        {
                            sb.AppendLine("  wrap failed: " + ex.GetType().Name + " - " + ex.Message);
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

        private static void Save(StringBuilder sb)
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Sports Interactive", "Football Manager 26", "FM26FullPlayerProbe");
                Directory.CreateDirectory(dir);
                string file = Path.Combine(dir, "sequentialnode_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".txt");
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
