using System;
using System.IO;
using System.Text;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using UnityEngine;
using UnityEngine.InputSystem;
using FM.UI;
using FM.GamePlugin;
using SI.Core;
using SI.Bindable;
using SI.Bindable.Reference.Core;

namespace FM26FullPlayerProbe
{
    [BepInPlugin("com.schumy12.fm26.fullplayerprobe", "FM26 Full Player Probe", "0.24.0")]
    public sealed class Plugin : BasePlugin
    {
        internal static new BepInEx.Logging.ManualLogSource Log;
        private static ProbeBehaviour _behaviour;

        public override void Load()
        {
            Log = base.Log;
            Log.LogInfo("[FM26FullProbe] Loaded v0.24 LIVE BACKEND CHANNEL PROBE - F8 starts a 32-person UniqueId test.");
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
        private const ulong KeyBase = 0xF026000000000000UL;
        private const uint UniqueIdProperty = 1970170212u;
        private const int ProbeCount = 32;

        private GameInteropSubsystem _interop;
        private ValueChangedWithSizeCallback _callback;
        private StringBuilder _sb;
        private bool _running;
        private float _finishAt;
        private int _callbackCount;
        private int _valueCount;

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
            _sb.AppendLine("=== FM26 FULL PLAYER PROBE 0.24 LIVE BACKEND CHANNEL ===");
            _sb.AppendLine("Direct GameInteropSubsystem.OpenChannel test; no Harmony and no UI traversal.");
            _sb.AppendLine("Property UniqueId=" + UniqueIdProperty);
            _sb.AppendLine();

            _callbackCount = 0;
            _valueCount = 0;
            _interop = FindLiveInterop(_sb);
            if (_interop == null)
            {
                _sb.AppendLine("RESULT: live GameInteropSubsystem NOT FOUND");
                Save(_sb, "livebackend");
                return;
            }

            _sb.AppendLine("Live GameInteropSubsystem ptr=0x" + _interop.Pointer.ToString("X"));

            try
            {
                System.Action<Il2CppSystem.ReadOnlySpan<ulong>, Il2CppSystem.Collections.Generic.List<TypedValue>, Il2CppSystem.ReadOnlySpan<long>> managed = OnChannelDataChanged;
                _callback = managed;
                _interop.add_OnChannelDataChange(_callback);
                _sb.AppendLine("Subscribed OnChannelDataChange=OK");
            }
            catch (Exception ex)
            {
                _sb.AppendLine("Subscribe failed: " + ex.GetType().Name + " - " + ex.Message);
                Save(_sb, "livebackend");
                return;
            }

            var property = new PropertyID(UniqueIdProperty);
            int opened = 0;
            for (int index = 0; index < ProbeCount; index++)
            {
                try
                {
                    var pr = new PersonReference(index);
                    var key = new Bindings.Key(KeyBase + (ulong)index + 1UL);
                    _interop.OpenChannel(pr, property, key);
                    opened++;
                    _sb.AppendLine("OPEN index=" + index + " key=" + (KeyBase + (ulong)index + 1UL) + " data1=" + pr.Data1);
                }
                catch (Exception ex)
                {
                    _sb.AppendLine("OPEN FAIL index=" + index + " " + ex.GetType().Name + " - " + ex.Message);
                }
            }

            _sb.AppendLine("opened=" + opened + "/" + ProbeCount);
            _sb.AppendLine("Waiting 3 seconds for backend responses...");
            _running = true;
            _finishAt = Time.unscaledTime + 3.0f;
        }

        private GameInteropSubsystem FindLiveInterop(StringBuilder sb)
        {
            try
            {
                var bindings = EmbeddedDataHandler.s_bindingSubsystem;
                if (bindings == null)
                {
                    sb.AppendLine("BindingSubsystem=<null>");
                    return null;
                }

                sb.AppendLine("BindingSubsystem ptr=0x" + bindings.Pointer.ToString("X"));
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

                    if (keyName == null) keyName = "";
                    if (!keyName.Contains("InteropReference")) continue;

                    sb.AppendLine("candidate registry key='" + keyName + "'");
                    var list = pair.Value;
                    if (list == null)
                    {
                        sb.AppendLine("  handler list=<null>");
                        continue;
                    }

                    sb.AppendLine("  handler list count=" + list.Count);
                    for (int i = 0; i < list.Count; i++)
                    {
                        var handler = list[i];
                        if (handler == null) continue;
                        sb.AppendLine("  handler[" + i + "] ptr=0x" + handler.Pointer.ToString("X"));

                        try
                        {
                            var concrete = new InteropDataHandler(handler.Pointer);
                            var interop = concrete.m_interop;
                            if (interop != null)
                            {
                                sb.AppendLine("  InteropDataHandler confirmed; interop=0x" + interop.Pointer.ToString("X"));
                                return interop;
                            }
                        }
                        catch (Exception ex)
                        {
                            sb.AppendLine("  concrete wrap/read failed: " + ex.GetType().Name + " - " + ex.Message);
                        }
                    }
                }

                sb.AppendLine("No InteropReference handler with live m_interop found.");
            }
            catch (Exception ex)
            {
                sb.AppendLine("FindLiveInterop failed: " + ex.GetType().Name + " - " + ex.Message);
            }
            return null;
        }

        private void OnChannelDataChanged(Il2CppSystem.ReadOnlySpan<ulong> keys, Il2CppSystem.Collections.Generic.List<TypedValue> values, Il2CppSystem.ReadOnlySpan<long> sizes)
        {
            try
            {
                _callbackCount++;
                int valueCount = values == null ? 0 : values.Count;
                _sb.AppendLine("CALLBACK #" + _callbackCount + " values=" + valueCount + " keysLen=" + keys.Length + " sizesLen=" + sizes.Length);

                int n = valueCount;
                if (keys.Length < n) n = keys.Length;
                for (int i = 0; i < n; i++)
                {
                    ulong key = keys[i];
                    if (key <= KeyBase || key > KeyBase + (ulong)ProbeCount) continue;

                    int index = (int)(key - KeyBase - 1UL);
                    var tv = values[i];
                    string type = "";
                    string text = "";
                    try { type = tv == null || tv.DataType == null ? "<null>" : tv.DataType.FullName; } catch (Exception ex) { type = "<" + ex.GetType().Name + ">"; }
                    try { text = tv == null ? "<null>" : (tv.AsString() ?? ""); } catch (Exception ex) { text = "<" + ex.GetType().Name + ": " + ex.Message + ">"; }
                    long size = i < sizes.Length ? sizes[i] : -1;

                    _valueCount++;
                    _sb.AppendLine("VALUE index=" + index + " key=" + key + " type=" + type + " text='" + text + "' size=" + size);
                }
            }
            catch (Exception ex)
            {
                try { _sb.AppendLine("CALLBACK ERROR: " + ex.GetType().Name + " - " + ex.Message); } catch { }
            }
        }

        private void FinishProbe()
        {
            if (!_running) return;
            _running = false;

            _sb.AppendLine();
            _sb.AppendLine("=== FINISH ===");
            _sb.AppendLine("callbackCount=" + _callbackCount + " matchingValues=" + _valueCount);

            if (_interop != null)
            {
                for (int index = 0; index < ProbeCount; index++)
                {
                    try
                    {
                        var key = new Bindings.Key(KeyBase + (ulong)index + 1UL);
                        _interop.CloseChannel(key);
                    }
                    catch (Exception ex)
                    {
                        _sb.AppendLine("CLOSE FAIL index=" + index + " " + ex.GetType().Name + " - " + ex.Message);
                    }
                }

                if (_callback != null)
                {
                    try
                    {
                        _interop.remove_OnChannelDataChange(_callback);
                        _sb.AppendLine("Unsubscribed callback=OK");
                    }
                    catch (Exception ex)
                    {
                        _sb.AppendLine("Unsubscribe failed: " + ex.GetType().Name + " - " + ex.Message);
                    }
                }
            }

            Save(_sb, "livebackend");
            _interop = null;
            _callback = null;
        }

        private static void Save(StringBuilder sb, string prefix)
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Sports Interactive", "Football Manager 26", "FM26FullPlayerProbe");
                Directory.CreateDirectory(dir);
                string file = Path.Combine(dir, prefix + "_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".txt");
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
