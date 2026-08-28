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
    [BepInPlugin("com.schumy12.fm26.fullplayerprobe", "FM26 Full Player Probe", "0.26.1")]
    public sealed class Plugin : BasePlugin
    {
        internal static new BepInEx.Logging.ManualLogSource Log;
        private static ProbeBehaviour _behaviour;

        public override void Load()
        {
            Log = base.Log;
            Log.LogInfo("[FM26FullProbe] Loaded v0.26.1 LIVE INTEROP HANDLER - m_bindingTree getter avoided.");
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
        private const ulong KeyBase = 0xF026100000000000UL;
        private const uint UniqueIdProperty = 1970170212u;
        private const int ProbeCount = 32;

        private InteropDataHandler _handler;
        private BindingSubsystem _bindings;
        private StringBuilder _sb;
        private bool _running;
        private float _finishAt;
        private readonly List<TypedValue> _sources = new List<TypedValue>();

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
            _sb.AppendLine("=== FM26 FULL PLAYER PROBE 0.26.1 LIVE INTEROP HANDLER ===");
            _sb.AppendLine("Uses live InteropDataHandler, but avoids its m_bindingTree getter because compile-time and runtime interop signatures differ.");
            _sb.AppendLine("Reads results through the already-live EmbeddedDataHandler.s_bindingSubsystem instead.");
            _sb.AppendLine("Property UniqueId=" + UniqueIdProperty);
            _sb.AppendLine();

            _sources.Clear();
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
                _sb.AppendLine("NOTE: m_bindingTree intentionally not touched.");
            }
            catch (Exception ex)
            {
                _sb.AppendLine("handler state read failed: " + ex.GetType().Name + " - " + ex.Message);
            }

            var property = new Bindings.Property("UniqueId", new PropertyID(UniqueIdProperty));
            int opened = 0;

            for (int index = 0; index < ProbeCount; index++)
            {
                try
                {
                    var pr = new PersonReference(index);
                    var tv = TypedValue.GetReferenceTypedValue();
                    tv.SetValue(pr);
                    _sources.Add(tv);

                    ulong rawKey = KeyBase + (ulong)index + 1UL;
                    var key = new Bindings.Key(rawKey);

                    _handler.OpenChannel(tv, property, key);
                    opened++;

                    string channelName = "<none>";
                    try
                    {
                        if (_handler.m_channels != null && _handler.m_channels.ContainsKey(rawKey))
                            channelName = _handler.m_channels[rawKey] ?? "<null>";
                    }
                    catch (Exception ex)
                    {
                        channelName = "<" + ex.GetType().Name + ">";
                    }

                    _sb.AppendLine("OPEN index=" + index + " key=" + rawKey + " data1=" + pr.Data1 + " channel='" + channelName + "'");
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

            int readable = 0;
            for (int index = 0; index < ProbeCount; index++)
            {
                ulong rawKey = KeyBase + (ulong)index + 1UL;
                var key = new Bindings.Key(rawKey);

                try
                {
                    var tv = _bindings.Get(ref key);
                    if (tv == null)
                    {
                        _sb.AppendLine("VALUE index=" + index + " key=" + rawKey + " <null>");
                        continue;
                    }

                    string type = SafeType(tv);
                    string text = SafeText(tv);
                    readable++;
                    _sb.AppendLine("VALUE index=" + index + " key=" + rawKey + " type=" + type + " text='" + text + "'");
                }
                catch (Exception ex)
                {
                    _sb.AppendLine("VALUE FAIL index=" + index + " key=" + rawKey + " " + ex.GetType().Name + " - " + ex.Message);
                }
            }

            _sb.AppendLine("readableValues=" + readable + "/" + ProbeCount);
            try { _sb.AppendLine("handler channels before close=" + (_handler.m_channels == null ? -1 : _handler.m_channels.Count)); } catch { }

            _sb.AppendLine();
            _sb.AppendLine("=== CLOSE ===");
            for (int index = 0; index < ProbeCount; index++)
            {
                try
                {
                    var key = new Bindings.Key(KeyBase + (ulong)index + 1UL);
                    _handler.CloseChannel(key);
                }
                catch (Exception ex)
                {
                    _sb.AppendLine("CLOSE FAIL index=" + index + " " + ex.GetType().Name + " - " + ex.Message);
                }
            }

            try { _sb.AppendLine("handler channels after close=" + (_handler.m_channels == null ? -1 : _handler.m_channels.Count)); } catch { }

            Save(_sb);
            _sources.Clear();
            _handler = null;
            _bindings = null;
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
                string file = Path.Combine(dir, "livehandler_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".txt");
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
