using System;
using System.IO;
using System.Text;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using UnityEngine;
using UnityEngine.InputSystem;
using FM.UI;
using SI.Bindable;
using SI.Core;

namespace FM26FullPlayerProbe
{
    [BepInPlugin("com.schumy12.fm26.fullplayerprobe", "FM26 Full Player Probe", "0.14.0")]
    public sealed class Plugin : BasePlugin
    {
        internal static new BepInEx.Logging.ManualLogSource Log;
        private static ProbeBehaviour _behaviour;

        public override void Load()
        {
            Log = base.Log;
            Log.LogInfo("[FM26FullProbe] Loaded v0.14 BINDING SNAPSHOT - open a player profile, then press F8. No Harmony/UI traversal.");
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

                if (Keyboard.current != null && Keyboard.current.f8Key.wasPressedThisFrame)
                    WriteBindingSnapshot();
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
                if (handlers == null) return;

                for (int i = 0; i < handlers.Length; i++)
                {
                    var h = handlers[i];
                    if (h == null) continue;

                    EmbeddedDataHandler.PersonReferenceClickedHandler ph = null;
                    try { ph = h.m_personReferenceClickedHandler; } catch { }
                    if (ph == null) continue;

                    _embedded = h;
                    _personHandler = ph;
                    Plugin.Log.LogInfo("[FM26FullProbe] Handler acquired: EmbeddedDataHandler=0x" + h.Pointer.ToString("X") + " personHandler=0x" + ph.Pointer.ToString("X"));
                    return;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[FM26FullProbe] Discovery failed: " + ex.GetType().Name + " - " + ex.Message);
            }
        }

        private void WriteBindingSnapshot()
        {
            if (_personHandler == null) DiscoverHandler();

            var sb = new StringBuilder();
            sb.AppendLine("=== FM26 FULL PLAYER PROBE 0.14 BINDING SNAPSHOT ===");
            sb.AppendLine("PersonReference.UID schema key=" + PersonReference.UID);
            sb.AppendLine("embeddedHandler=" + (_embedded == null ? "<null>" : ("0x" + _embedded.Pointer.ToString("X"))));
            sb.AppendLine("personHandler=" + (_personHandler == null ? "<null>" : ("0x" + _personHandler.Pointer.ToString("X"))));

            BindingSubsystem bs = null;
            try
            {
                bs = EmbeddedDataHandler.s_bindingSubsystem;
                sb.AppendLine("BindingSubsystem=" + (bs == null ? "<null>" : ("0x" + bs.Pointer.ToString("X"))));
            }
            catch (Exception ex)
            {
                sb.AppendLine("BindingSubsystem read failed: " + ex.GetType().Name + " - " + ex.Message);
            }

            if (_personHandler != null)
            {
                try { sb.AppendLine("playerParamName='" + (_personHandler.m_playerParamName ?? "") + "'"); }
                catch (Exception ex) { sb.AppendLine("playerParamName read failed: " + ex.GetType().Name); }

                try
                {
                    var rec = _personHandler.m_record;
                    sb.AppendLine("current m_record=" + (rec == null ? "<null>" : ("0x" + rec.Pointer.ToString("X") + " count=" + rec.Count)));
                }
                catch (Exception ex)
                {
                    sb.AppendLine("current m_record read failed: " + ex.GetType().Name + " - " + ex.Message);
                }

                try
                {
                    var rootKey = _personHandler.m_rootKey;
                    sb.AppendLine("m_rootKey=" + SafeKeyText(rootKey));
                    if (bs != null) DumpBindingValue(sb, bs, ref rootKey, "rootKey");
                }
                catch (Exception ex)
                {
                    sb.AppendLine("m_rootKey read/get failed: " + ex.GetType().Name + " - " + ex.Message);
                }

                try
                {
                    var viewKey = _personHandler.m_viewKey;
                    sb.AppendLine("m_viewKey=" + SafeKeyText(viewKey));
                    if (bs != null) DumpBindingValue(sb, bs, ref viewKey, "viewKey");
                }
                catch (Exception ex)
                {
                    sb.AppendLine("m_viewKey read/get failed: " + ex.GetType().Name + " - " + ex.Message);
                }

                try
                {
                    var requests = _personHandler.m_dataRequests;
                    sb.AppendLine("m_dataRequests=" + (requests == null ? "<null>" : ("ptr=0x" + requests.Pointer.ToString("X") + " count=" + requests.Count)));
                }
                catch (Exception ex)
                {
                    sb.AppendLine("m_dataRequests read failed: " + ex.GetType().Name + " - " + ex.Message);
                }

                try
                {
                    var cancelled = _personHandler.m_cancelledRequests;
                    sb.AppendLine("m_cancelledRequests=" + (cancelled == null ? "<null>" : ("ptr=0x" + cancelled.Pointer.ToString("X") + " count=" + cancelled.Count)));
                }
                catch (Exception ex)
                {
                    sb.AppendLine("m_cancelledRequests read failed: " + ex.GetType().Name + " - " + ex.Message);
                }
            }

            Save(sb);
        }

        private static void DumpBindingValue(StringBuilder sb, BindingSubsystem bs, ref Bindings.Key key, string label)
        {
            try
            {
                var tv = bs.Get(ref key);
                if (tv == null)
                {
                    sb.AppendLine(label + " Get=<null>");
                    return;
                }

                string dt = "?";
                try { dt = tv.DataType == null ? "<null>" : tv.DataType.FullName; } catch { }
                string text = "";
                try { text = tv.AsString() ?? ""; } catch (Exception ex) { text = "<AsString failed: " + ex.GetType().Name + ">"; }
                sb.AppendLine(label + " Get ptr=0x" + tv.Pointer.ToString("X") + " type=" + dt + " text='" + text + "'");
            }
            catch (Exception ex)
            {
                sb.AppendLine(label + " Get failed: " + ex.GetType().Name + " - " + ex.Message);
            }
        }

        private static string SafeKeyText(Bindings.Key key)
        {
            try { return key.ToString(); }
            catch { return "<ToString failed>"; }
        }

        private static void Save(StringBuilder sb)
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Sports Interactive", "Football Manager 26", "FM26FullPlayerProbe");
                Directory.CreateDirectory(dir);
                string file = Path.Combine(dir, "bindingsnapshot_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".txt");
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
