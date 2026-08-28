using System;
using System.IO;
using System.Text;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using UnityEngine;
using UnityEngine.InputSystem;
using FM.UI;

namespace FM26FullPlayerProbe
{
    [BepInPlugin("com.schumy12.fm26.fullplayerprobe", "FM26 Full Player Probe", "0.16.0")]
    public sealed class Plugin : BasePlugin
    {
        internal static new BepInEx.Logging.ManualLogSource Log;
        private static ProbeBehaviour _behaviour;

        public override void Load()
        {
            Log = base.Log;
            Log.LogInfo("[FM26FullProbe] Loaded v0.16 PACKED INDEX PROBE - press F8 after loading a save.");
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
                    RunProbe();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("[FM26FullProbe] Update error: " + ex);
            }
        }

        private void RunProbe()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== FM26 FULL PLAYER PROBE 0.16 PACKED INDEX PROBE ===");
            sb.AppendLine("Previous result: raw PersonReference indices 0..1999 constructed but produced zero UID hits.");
            sb.AppendLine("Testing whether the constructor expects a packed DatabaseTableType + row index.");
            sb.AppendLine("PersonReference.UID schema key=" + PersonReference.UID);
            sb.AppendLine();

            sb.AppendLine("=== DatabaseTableType ENUM ===");
            string[] names = null;
            Array values = null;
            try
            {
                names = System.Enum.GetNames(typeof(DatabaseTableType));
                values = System.Enum.GetValues(typeof(DatabaseTableType));
                int n = Math.Min(names.Length, values.Length);
                for (int i = 0; i < n; i++)
                {
                    int v = Convert.ToInt32(values.GetValue(i));
                    sb.AppendLine("ENUM name='" + names[i] + "' value=" + v);
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine("Enum dump failed: " + ex.GetType().Name + " - " + ex.Message);
            }

            sb.AppendLine();
            sb.AppendLine("=== RAW CONSTRUCTOR DECODE ===");
            for (int raw = 0; raw < 4; raw++)
                DumpReferenceShape(sb, raw, "RAW");

            sb.AppendLine();
            sb.AppendLine("=== PACKED CANDIDATE TESTS ===");
            int[] shifts = new[] { 16, 20, 24, 28 };
            int hits = 0;
            int attempts = 0;

            if (names != null && values != null)
            {
                int n = Math.Min(names.Length, values.Length);
                for (int i = 0; i < n && hits < 30; i++)
                {
                    string name = names[i] ?? "";
                    if (name.IndexOf("person", StringComparison.OrdinalIgnoreCase) < 0 &&
                        name.IndexOf("player", StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    int tableValue;
                    try { tableValue = Convert.ToInt32(values.GetValue(i)); }
                    catch { continue; }

                    foreach (int shift in shifts)
                    {
                        for (int index = 0; index < 64 && hits < 30; index++)
                        {
                            int combined = unchecked((tableValue << shift) | index);
                            attempts++;
                            if (TryCandidate(sb, name, tableValue, shift, index, combined))
                                hits++;
                        }
                    }
                }
            }

            sb.AppendLine();
            sb.AppendLine("SUMMARY packedAttempts=" + attempts + " uidHits=" + hits);
            Save(sb);
        }

        private static void DumpReferenceShape(StringBuilder sb, int combined, string label)
        {
            try
            {
                var pr = new PersonReference(combined);
                if (pr == null)
                {
                    sb.AppendLine(label + " combined=" + combined + " -> <null>");
                    return;
                }

                string typeText = "?";
                string indexText = "?";
                string combinedText = "?";
                string data1Text = "?";
                try { typeText = pr.Type.ToString(); } catch (Exception ex) { typeText = "<" + ex.GetType().Name + ">"; }
                try { indexText = pr.m_index.ToString(); } catch (Exception ex) { indexText = "<" + ex.GetType().Name + ">"; }
                try { combinedText = pr.CombinedIndexAndType.ToString(); } catch (Exception ex) { combinedText = "<" + ex.GetType().Name + ">"; }
                try { data1Text = pr.Data1.ToString(); } catch (Exception ex) { data1Text = "<" + ex.GetType().Name + ">"; }

                sb.AppendLine(label + " input=" + combined + " type=" + typeText + " m_index=" + indexText + " combined=" + combinedText + " Data1=" + data1Text);
            }
            catch (Exception ex)
            {
                sb.AppendLine(label + " combined=" + combined + " ctor/shape failed: " + ex.GetType().Name + " - " + ex.Message);
            }
        }

        private static bool TryCandidate(StringBuilder sb, string tableName, int tableValue, int shift, int index, int combined)
        {
            try
            {
                var pr = new PersonReference(combined);
                if (pr == null) return false;

                int uid;
                bool ok = pr.TryGetValue(PersonReference.UID, out uid);
                if (!ok) return false;

                string typeText = "?";
                string realIndex = "?";
                try { typeText = pr.Type.ToString(); } catch { }
                try { realIndex = pr.m_index.ToString(); } catch { }

                sb.AppendLine("HIT table='" + tableName + "' tableValue=" + tableValue + " shift=" + shift + " index=" + index + " combined=" + combined + " decodedType=" + typeText + " decodedIndex=" + realIndex + " uid=" + uid + " ptr=0x" + pr.Pointer.ToString("X"));
                return true;
            }
            catch (Exception ex)
            {
                if (index == 0)
                    sb.AppendLine("candidate table='" + tableName + "' shift=" + shift + " failed: " + ex.GetType().Name + " - " + ex.Message);
                return false;
            }
        }

        private static void Save(StringBuilder sb)
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Sports Interactive", "Football Manager 26", "FM26FullPlayerProbe");
                Directory.CreateDirectory(dir);
                string file = Path.Combine(dir, "packedindexprobe_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".txt");
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
