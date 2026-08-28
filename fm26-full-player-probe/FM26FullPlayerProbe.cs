using System;
using System.IO;
using System.Text;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using UnityEngine;
using UnityEngine.InputSystem;
using FM.UI;
using SI.Core;

namespace FM26FullPlayerProbe
{
    [BepInPlugin("com.schumy12.fm26.fullplayerprobe", "FM26 Full Player Probe", "0.13.0")]
    public sealed class Plugin : BasePlugin
    {
        internal static new BepInEx.Logging.ManualLogSource Log;
        private static ProbeBehaviour _behaviour;

        public override void Load()
        {
            Log = base.Log;
            Log.LogInfo("[FM26FullProbe] Loaded v0.13 PASSIVE TRANSIENT CAPTURE - open Database giocatori, then click a player. F8 writes status. No Harmony/UI traversal.");
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
        private EmbeddedDataHandler _embedded;
        private EmbeddedDataHandler.PersonReferenceClickedHandler _personHandler;
        private int _nextDiscoveryFrame;
        private IntPtr _lastRecordPointer = IntPtr.Zero;
        private bool _capturedAny;
        private int _captureSequence;

        public ProbeBehaviour(IntPtr ptr) : base(ptr) { }

        private void Update()
        {
            try
            {
                if (_personHandler == null && Time.frameCount >= _nextDiscoveryFrame)
                {
                    _nextDiscoveryFrame = Time.frameCount + 30;
                    DiscoverHandler();
                }

                if (_personHandler != null)
                    PollTransientRecord();

                if (Keyboard.current != null && Keyboard.current.f8Key.wasPressedThisFrame)
                    WriteStatusSnapshot();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("[FM26FullProbe] Update error: " + ex);
            }
        }

        private void DiscoverHandler()
        {
            try
            {
                var handlers = Resources.FindObjectsOfTypeAll<EmbeddedDataHandler>();
                if (handlers == null || handlers.Length == 0) return;

                for (int i = 0; i < handlers.Length; i++)
                {
                    var h = handlers[i];
                    if (h == null) continue;

                    EmbeddedDataHandler.PersonReferenceClickedHandler ph = null;
                    try { ph = h.m_personReferenceClickedHandler; } catch { }
                    if (ph == null) continue;

                    _embedded = h;
                    _personHandler = ph;
                    Plugin.Log.LogInfo("[FM26FullProbe] Passive handler acquired: EmbeddedDataHandler=0x" + h.Pointer.ToString("X") + " personHandler=0x" + ph.Pointer.ToString("X"));
                    return;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[FM26FullProbe] Handler discovery failed: " + ex.GetType().Name + " - " + ex.Message);
            }
        }

        private void PollTransientRecord()
        {
            Record rec = null;
            try { rec = _personHandler.m_record; }
            catch
            {
                _personHandler = null;
                _embedded = null;
                return;
            }

            if (rec == null)
            {
                _lastRecordPointer = IntPtr.Zero;
                return;
            }

            IntPtr ptr;
            try { ptr = rec.Pointer; }
            catch { return; }

            // Avoid writing the same transient record repeatedly while it remains alive.
            if (ptr == IntPtr.Zero || ptr == _lastRecordPointer) return;
            _lastRecordPointer = ptr;

            CaptureRecord(rec, ptr);
        }

        private void CaptureRecord(Record rec, IntPtr ptr)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== FM26 FULL PLAYER PROBE 0.13 TRANSIENT RECORD CAPTURE ===");
            sb.AppendLine("Captured automatically while PersonReferenceClickedHandler.m_record was non-null.");
            sb.AppendLine("record ptr=0x" + ptr.ToString("X"));
            try { sb.AppendLine("record count=" + rec.Count); } catch (Exception ex) { sb.AppendLine("record count read failed: " + ex.GetType().Name); }
            sb.AppendLine("PersonReference.UID schema key=" + PersonReference.UID);

            try
            {
                TypedValue uidValue;
                bool ok = rec.TryGetValue(PersonReference.UID, out uidValue);
                sb.AppendLine("record.TryGetValue(PersonReference.UID)=" + ok);
                if (ok && uidValue != null)
                {
                    string dt = "?";
                    try { dt = uidValue.DataType == null ? "<null>" : uidValue.DataType.FullName; } catch { }
                    string text = "";
                    try { text = uidValue.AsString() ?? ""; } catch (Exception ex) { text = "<AsString failed: " + ex.GetType().Name + ">"; }
                    sb.AppendLine("UID VALUE type=" + dt + " text='" + text + "' ptr=0x" + uidValue.Pointer.ToString("X"));
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine("UID lookup failed: " + ex.GetType().Name + " - " + ex.Message);
            }

            // Only when the backing record is known to be alive, ask the handler for its data reference.
            try
            {
                var tv = _personHandler.DataReferenceAsTypedValue;
                if (tv == null)
                {
                    sb.AppendLine("DataReferenceAsTypedValue=<null>");
                }
                else
                {
                    string dt = "?";
                    try { dt = tv.DataType == null ? "<null>" : tv.DataType.FullName; } catch { }
                    string text = "";
                    try { text = tv.AsString() ?? ""; } catch (Exception ex) { text = "<AsString failed: " + ex.GetType().Name + ">"; }
                    sb.AppendLine("DataReferenceAsTypedValue type=" + dt + " text='" + text + "' ptr=0x" + tv.Pointer.ToString("X"));
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine("DataReferenceAsTypedValue read failed: " + ex.GetType().Name + " - " + ex.Message);
            }

            _capturedAny = true;
            _captureSequence++;
            SaveCapture(sb, "transientcapture_" + _captureSequence.ToString("D3") + "_");
        }

        private void WriteStatusSnapshot()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== FM26 FULL PLAYER PROBE 0.13 STATUS ===");
            sb.AppendLine("capturedAny=" + _capturedAny);
            sb.AppendLine("captureSequence=" + _captureSequence);
            sb.AppendLine("embeddedHandler=" + (_embedded == null ? "<null>" : ("0x" + _embedded.Pointer.ToString("X"))));
            sb.AppendLine("personHandler=" + (_personHandler == null ? "<null>" : ("0x" + _personHandler.Pointer.ToString("X"))));
            sb.AppendLine("PersonReference.UID schema key=" + PersonReference.UID);

            if (_personHandler != null)
            {
                try
                {
                    var rec = _personHandler.m_record;
                    sb.AppendLine("current m_record=" + (rec == null ? "<null>" : ("0x" + rec.Pointer.ToString("X"))));
                }
                catch (Exception ex)
                {
                    sb.AppendLine("current m_record read failed: " + ex.GetType().Name + " - " + ex.Message);
                }
            }

            try
            {
                var bs = EmbeddedDataHandler.s_bindingSubsystem;
                sb.AppendLine("BindingSubsystem=" + (bs == null ? "<null>" : ("0x" + bs.Pointer.ToString("X"))));
            }
            catch (Exception ex)
            {
                sb.AppendLine("BindingSubsystem read failed: " + ex.GetType().Name + " - " + ex.Message);
            }

            SaveCapture(sb, "status_");
        }

        private static void SaveCapture(StringBuilder sb, string prefix)
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Sports Interactive", "Football Manager 26", "FM26FullPlayerProbe");
                Directory.CreateDirectory(dir);
                string file = Path.Combine(dir, prefix + System.DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".txt");
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
