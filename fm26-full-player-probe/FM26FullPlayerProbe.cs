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
    [BepInPlugin("com.schumy12.fm26.fullplayerprobe", "FM26 Full Player Probe", "0.15.1")]
    public sealed class Plugin : BasePlugin
    {
        internal static new BepInEx.Logging.ManualLogSource Log;
        private static ProbeBehaviour _behaviour;

        public override void Load()
        {
            Log = base.Log;
            Log.LogInfo("[FM26FullProbe] Loaded v0.15.1 DIRECT PERSON INDEX PROBE - press F8 anywhere after loading a save. No Harmony/UI traversal.");
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
                    RunIndexProbe();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("[FM26FullProbe] Update error: " + ex);
            }
        }

        private void RunIndexProbe()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== FM26 FULL PLAYER PROBE 0.15.1 DIRECT PERSON INDEX PROBE ===");
            sb.AppendLine("Goal: test whether PersonReference(int index) + TryGetValue(UID) can enumerate database persons directly.");
            sb.AppendLine("PersonReference.UID schema key=" + PersonReference.UID);
            sb.AppendLine("Range: index 0..1999, stop after 100 successful UID reads.");
            sb.AppendLine();

            int created = 0;
            int ctorErrors = 0;
            int tryErrors = 0;
            int hits = 0;

            for (int index = 0; index < 2000 && hits < 100; index++)
            {
                PersonReference pr = null;
                try
                {
                    pr = new PersonReference(index);
                    created++;
                }
                catch (Exception ex)
                {
                    ctorErrors++;
                    if (ctorErrors <= 10)
                        sb.AppendLine("index=" + index + " ctor failed: " + ex.GetType().Name + " - " + ex.Message);
                    continue;
                }

                if (pr == null)
                {
                    sb.AppendLine("index=" + index + " constructor returned null");
                    continue;
                }

                try
                {
                    int uid;
                    bool ok = pr.TryGetValue(PersonReference.UID, out uid);
                    if (ok)
                    {
                        hits++;
                        sb.AppendLine("HIT index=" + index + " uid=" + uid + " ptr=0x" + pr.Pointer.ToString("X"));
                    }
                    else if (index < 20)
                    {
                        sb.AppendLine("MISS index=" + index + " uid=" + uid);
                    }
                }
                catch (Exception ex)
                {
                    tryErrors++;
                    if (tryErrors <= 20)
                        sb.AppendLine("index=" + index + " TryGetValue failed: " + ex.GetType().Name + " - " + ex.Message);
                }
            }

            sb.AppendLine();
            sb.AppendLine("SUMMARY created=" + created + " hits=" + hits + " ctorErrors=" + ctorErrors + " tryErrors=" + tryErrors);

            Save(sb);
        }

        private static void Save(StringBuilder sb)
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Sports Interactive", "Football Manager 26", "FM26FullPlayerProbe");
                Directory.CreateDirectory(dir);
                string file = Path.Combine(dir, "indexprobe_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".txt");
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
