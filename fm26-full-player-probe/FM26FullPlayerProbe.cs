using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
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
    [BepInPlugin("com.schumy12.fm26.fullplayerprobe", "FM26 Full Player Probe", "0.28.0")]
    public sealed class Plugin : BasePlugin
    {
        internal static new BepInEx.Logging.ManualLogSource Log;
        private static ProbeBehaviour _behaviour;

        public override void Load()
        {
            Log = base.Log;
            Log.LogInfo("[FM26FullProbe] Loaded v0.28 VALID REGISTERED NODES - press F8 after loading a save.");
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

        private InteropDataHandler _handler;
        private BindingSubsystem _bindings;
        private StringBuilder _sb;
        private bool _running;
        private float _finishAt;
        private readonly List<TypedValue> _sources = new List<TypedValue>();
        private readonly List<Bindings.Key> _keys = new List<Bindings.Key>();

        public ProbeBehaviour(IntPtr ptr) : base(ptr) { }

        private void Update()
        {
            try
            {
                if (!_running && Keyboard.current != null && Keyboard.current.f8Key.wasPressedThisFrame)
                    StartProbe();

                if (_running && Time.unscaledTime >= _finishAt)
                    FinishProbe();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("[FM26FullProbe] Update error: " + ex);
                if (_running)
                {
                    try { _sb?.AppendLine("UPDATE/FATAL: " + ex); } catch { }
                    FinishProbe();
                }
            }
        }

        private void StartProbe()
        {
            _sb = new StringBuilder();
            _sb.AppendLine("=== FM26 FULL PLAYER PROBE 0.28 VALID REGISTERED NODES ===");
            _sb.AppendLine("Creates real temporary Bindings nodes before asking the live InteropDataHandler for UniqueId.");
            _sb.AppendLine("No managed backend callback, no Harmony, no UI traversal, no m_bindingTree getter.");
            _sb.AppendLine("Property UniqueId=" + UniqueIdProperty);
            _sb.AppendLine();

            _sources.Clear();
            _keys.Clear();

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

            var property = new Bindings.Property("UniqueId", new PropertyID(UniqueIdProperty));
            int opened = 0;
            string runTag = DateTime.Now.ToString("HHmmssfff");

            for (int index = 0; index < ProbeCount; index++)
            {
                try
                {
                    var pr = new PersonReference(index);
                    var tv = TypedValue.GetReferenceTypedValue();
                    tv.SetValue(pr);
                    _sources.Add(tv);

                    string nodeName = "__fm26probe_uid_" + runTag + "_" + index;
                    var key = CreateTemporaryNode(_bindings, nodeName);
                    _keys.Add(key);

                    bool valid = false;
                    bool exists = false;
                    try { valid = key.IsValid(); } catch { }
                    try { exists = _bindings.Exists(ref key); } catch { }

                    _handler.OpenChannel(tv, property, key);
                    opened++;

                    string channelName = "<none>";
                    try
                    {
                        ulong rawKey = key;
                        if (_handler.m_channels != null && _handler.m_channels.ContainsKey(rawKey))
                            channelName = _handler.m_channels[rawKey] ?? "<null>";
                    }
                    catch (Exception ex)
                    {
                        channelName = "<" + ex.GetType().Name + ">";
                    }

                    _sb.AppendLine("OPEN index=" + index + " key=" + SafeKey(key) + " valid=" + valid + " exists=" + exists + " data1=" + pr.Data1 + " channel='" + channelName + "'");
                }
                catch (Exception ex)
                {
                    _sb.AppendLine("OPEN FAIL index=" + index + " " + ex.GetType().Name + " - " + ex.Message);
                }
            }

            _sb.AppendLine("opened=" + opened + "/" + ProbeCount);
            try { _sb.AppendLine("handler channels after open=" + (_handler.m_channels == null ? -1 : _handler.m_channels.Count)); } catch { }
            _sb.AppendLine("Waiting 3 seconds for native InteropDataHandler callback...");

            _running = true;
            _finishAt = Time.unscaledTime + 3.0f;
        }

        private void FinishProbe()
        {
            if (!_running) return;
            _running = false;

            _sb.AppendLine();
            _sb.AppendLine("=== VALUES AFTER WAIT ===");

            int setCount = 0;
            int readable = 0;

            for (int index = 0; index < _keys.Count; index++)
            {
                var key = _keys[index];
                try
                {
                    bool exists = _bindings.Exists(ref key);
                    bool isSet = _bindings.IsDataSet(key);
                    if (isSet) setCount++;

                    if (!exists || !isSet)
                    {
                        _sb.AppendLine("VALUE index=" + index + " key=" + SafeKey(key) + " exists=" + exists + " isDataSet=" + isSet);
                        continue;
                    }

                    var tv = _bindings.Get(ref key);
                    if (tv == null)
                    {
                        _sb.AppendLine("VALUE index=" + index + " key=" + SafeKey(key) + " exists=true isDataSet=true <null>");
                        continue;
                    }

                    readable++;
                    _sb.AppendLine("VALUE index=" + index + " key=" + SafeKey(key) + " exists=true isDataSet=true type=" + SafeType(tv) + " text='" + SafeText(tv) + "'");
                }
                catch (Exception ex)
                {
                    _sb.AppendLine("VALUE FAIL index=" + index + " key=" + SafeKey(key) + " " + ex.GetType().Name + " - " + ex.Message);
                }
            }

            _sb.AppendLine("dataSetValues=" + setCount + "/" + _keys.Count);
            _sb.AppendLine("readableValues=" + readable + "/" + _keys.Count);
            try { _sb.AppendLine("handler channels before close=" + (_handler.m_channels == null ? -1 : _handler.m_channels.Count)); } catch { }

            _sb.AppendLine();
            _sb.AppendLine("=== CLEANUP ===");
            for (int index = 0; index < _keys.Count; index++)
            {
                var key = _keys[index];
                try { _handler.CloseChannel(key); }
                catch (Exception ex) { _sb.AppendLine("CLOSE FAIL index=" + index + " " + ex.GetType().Name + " - " + ex.Message); }

                try { _bindings.Remove(ref key); }
                catch (Exception ex) { _sb.AppendLine("REMOVE FAIL index=" + index + " " + ex.GetType().Name + " - " + ex.Message); }
            }

            try { _sb.AppendLine("handler channels after cleanup=" + (_handler.m_channels == null ? -1 : _handler.m_channels.Count)); } catch { }

            Save(_sb);
            _sources.Clear();
            _keys.Clear();
            _handler = null;
            _bindings = null;
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
                string file = Path.Combine(dir, "validnode_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".txt");
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
