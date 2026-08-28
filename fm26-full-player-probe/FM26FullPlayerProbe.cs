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
    [BepInPlugin("com.schumy12.fm26.fullplayerprobe", "FM26 Full Player Probe", "0.12.0")]
    public sealed class Plugin : BasePlugin
    {
        internal static new BepInEx.Logging.ManualLogSource Log;
        private static ProbeBehaviour _behaviour;

        public override void Load()
        {
            Log = base.Log;
            Log.LogInfo("[FM26FullProbe] Loaded v0.12 TARGETED - open player profile, press F8. No Harmony, no UI traversal.");
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
        public ProbeBehaviour(IntPtr ptr) : base(ptr) { }

        private void Update()
        {
            try
            {
                if (Keyboard.current != null && Keyboard.current.f8Key.wasPressedThisFrame)
                    ProbeRuntime();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("[FM26FullProbe] " + ex);
            }
        }

        private void ProbeRuntime()
        {
            var sb = new StringBuilder();
            Line(sb, "=== FM26 FULL PLAYER PROBE 0.12 TARGETED RUNTIME ===");
            Line(sb, "No Harmony. No VisualElement traversal. Direct known IL2CPP wrappers only.");
            Line(sb, "PersonReference.UID schema key=" + PersonReference.UID);

            Line(sb, "\n=== EXACT LIVE EmbeddedDataHandler SEARCH ===");
            ProbeEmbeddedDataHandlers(sb);

            Line(sb, "\n=== STATIC BindingSubsystem ===");
            ProbeBindingSubsystem(sb);

            Save(sb);
        }

        private static void ProbeEmbeddedDataHandlers(StringBuilder sb)
        {
            EmbeddedDataHandler[] handlers = null;
            try { handlers = Resources.FindObjectsOfTypeAll<EmbeddedDataHandler>(); }
            catch (Exception ex)
            {
                Line(sb, "FindObjectsOfTypeAll<EmbeddedDataHandler> failed: " + ex.GetType().Name + " - " + ex.Message);
                return;
            }

            int count = handlers == null ? 0 : handlers.Length;
            Line(sb, "EmbeddedDataHandler count=" + count);
            if (handlers == null) return;

            for (int i = 0; i < handlers.Length; i++)
            {
                var h = handlers[i];
                if (h == null) continue;

                string name = "";
                try { name = h.name ?? ""; } catch { }
                Line(sb, "HANDLER[" + i + "] name='" + name + "' ptr=0x" + h.Pointer.ToString("X"));

                EmbeddedDataHandler.PersonReferenceClickedHandler ph = null;
                try
                {
                    ph = h.m_personReferenceClickedHandler;
                    Line(sb, "  personClickedHandler=" + (ph == null ? "<null>" : ("ptr=0x" + ph.Pointer.ToString("X"))));
                }
                catch (Exception ex)
                {
                    Line(sb, "  personClickedHandler read failed: " + ex.GetType().Name + " - " + ex.Message);
                    continue;
                }

                if (ph == null) continue;

                try { Line(sb, "  playerParamName='" + (ph.m_playerParamName ?? "") + "'"); }
                catch (Exception ex) { Line(sb, "  playerParamName read failed: " + ex.GetType().Name); }

                Record rec = null;
                try
                {
                    rec = ph.m_record;
                    Line(sb, "  m_record=" + (rec == null ? "<null>" : ("ptr=0x" + rec.Pointer.ToString("X") + " count=" + rec.Count)));
                }
                catch (Exception ex)
                {
                    Line(sb, "  m_record read failed: " + ex.GetType().Name + " - " + ex.Message);
                }

                if (rec != null)
                    ProbeUidFromRecord(sb, rec, "  ");

                try
                {
                    var tv = ph.DataReferenceAsTypedValue;
                    if (tv == null)
                    {
                        Line(sb, "  DataReferenceAsTypedValue=<null>");
                    }
                    else
                    {
                        string dt = "?";
                        try { dt = tv.DataType == null ? "<null>" : tv.DataType.FullName; } catch { }
                        string text = "";
                        try { text = tv.AsString() ?? ""; } catch { text = "<AsString failed>"; }
                        Line(sb, "  DataReferenceAsTypedValue ptr=0x" + tv.Pointer.ToString("X") + " type=" + dt + " text='" + text + "'");
                    }
                }
                catch (Exception ex)
                {
                    Line(sb, "  DataReferenceAsTypedValue read failed: " + ex.GetType().Name + " - " + ex.Message);
                }
            }
        }

        private static void ProbeUidFromRecord(StringBuilder sb, Record rec, string pad)
        {
            try
            {
                TypedValue uidValue;
                bool ok = rec.TryGetValue(PersonReference.UID, out uidValue);
                Line(sb, pad + "record.TryGetValue(PersonReference.UID=" + PersonReference.UID + ")=" + ok);
                if (!ok || uidValue == null) return;

                string dt = "?";
                try { dt = uidValue.DataType == null ? "<null>" : uidValue.DataType.FullName; } catch { }
                string text = "";
                try { text = uidValue.AsString() ?? ""; } catch { text = "<AsString failed>"; }
                Line(sb, pad + "UID VALUE type=" + dt + " text='" + text + "' ptr=0x" + uidValue.Pointer.ToString("X"));
            }
            catch (Exception ex)
            {
                Line(sb, pad + "UID lookup failed: " + ex.GetType().Name + " - " + ex.Message);
            }
        }

        private static void ProbeBindingSubsystem(StringBuilder sb)
        {
            try
            {
                var bs = EmbeddedDataHandler.s_bindingSubsystem;
                if (bs == null)
                {
                    Line(sb, "EmbeddedDataHandler.s_bindingSubsystem=<null>");
                    return;
                }

                Line(sb, "BindingSubsystem ptr=0x" + bs.Pointer.ToString("X"));
                try
                {
                    var ds = bs.DataSet;
                    Line(sb, "BindingSubsystem.DataSet count=" + (ds == null ? -1 : ds.Count));
                }
                catch (Exception ex)
                {
                    Line(sb, "BindingSubsystem.DataSet read failed: " + ex.GetType().Name + " - " + ex.Message);
                }
            }
            catch (Exception ex)
            {
                Line(sb, "EmbeddedDataHandler.s_bindingSubsystem read failed: " + ex.GetType().Name + " - " + ex.Message);
            }
        }

        private static void Line(StringBuilder sb, string s)
        {
            sb.AppendLine(s);
            try { Plugin.Log.LogInfo("[FM26FullProbe] " + s); } catch { }
        }

        private static void Save(StringBuilder sb)
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Sports Interactive", "Football Manager 26", "FM26FullPlayerProbe");
                Directory.CreateDirectory(dir);
                string file = Path.Combine(dir, "runtimeprobe_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt");
                File.WriteAllText(file, sb.ToString(), Encoding.UTF8);
                Plugin.Log.LogInfo("[FM26FullProbe] Saved runtime probe: " + file);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("[FM26FullProbe] Save failed: " + ex);
            }
        }
    }
}
