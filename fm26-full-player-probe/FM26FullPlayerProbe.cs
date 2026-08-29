using System;
using System.IO;
using System.Text;
using System.Reflection;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using UnityEngine;
using UnityEngine.InputSystem;
using FM.GamePlugin;

namespace FM26FullPlayerProbe
{
    [BepInPlugin("com.schumy12.fm26.fullplayerprobe", "FM26 Full Player Probe", "0.36.0")]
    public sealed class Plugin : BasePlugin
    {
        internal static new BepInEx.Logging.ManualLogSource Log;
        private static ProbeBehaviour _behaviour;

        public override void Load()
        {
            Log = base.Log;
            Log.LogInfo("[FM26FullProbe] Loaded v0.36 GAMEINTEROP COMPILE-SIGNATURE PROBE - press F8.");
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
            if (Keyboard.current == null || !Keyboard.current.f8Key.wasPressedThisFrame) return;

            var sb = new StringBuilder();
            sb.AppendLine("=== FM26 FULL PLAYER PROBE 0.36 GAMEINTEROP COMPILE-SIGNATURES ===");
            sb.AppendLine("Purpose: discover the exact method signatures exposed by the build-time FM.GamePlugin wrapper.");
            sb.AppendLine("Metadata reflection only. No generated method/property is invoked through reflection.");
            sb.AppendLine();

            try
            {
                DumpType(sb, typeof(GameInteropSubsystem));
            }
            catch (Exception ex)
            {
                sb.AppendLine("FATAL: " + ex);
            }

            Save(sb);
        }

        private static void DumpType(StringBuilder sb, Type t)
        {
            sb.AppendLine("TYPE " + t.FullName);
            sb.AppendLine("Assembly=" + t.Assembly.FullName);
            sb.AppendLine();

            string[] wanted =
            {
                "AddNode", "RemoveNode", "AddData", "ReleaseData", "SetTarget", "SetData",
                "OpenChannel", "CloseChannel", "UpdateChannelData"
            };

            var methods = t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            for (int w = 0; w < wanted.Length; w++)
            {
                string name = wanted[w];
                sb.AppendLine("--- " + name + " ---");
                int count = 0;
                for (int i = 0; i < methods.Length; i++)
                {
                    var m = methods[i];
                    if (m.Name != name) continue;
                    count++;
                    sb.AppendLine(FormatMethod(m));
                }
                if (count == 0) sb.AppendLine("<no overload exposed>");
                sb.AppendLine();
            }
        }

        private static string FormatMethod(MethodInfo m)
        {
            var sb = new StringBuilder();
            sb.Append(m.IsPublic ? "public " : (m.IsPrivate ? "private " : "nonpublic "));
            if (m.IsStatic) sb.Append("static ");
            sb.Append(FormatType(m.ReturnType));
            sb.Append(" ");
            sb.Append(m.Name);
            sb.Append("(");

            var ps = m.GetParameters();
            for (int i = 0; i < ps.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                var p = ps[i];
                if (p.IsOut) sb.Append("out ");
                else if (p.ParameterType.IsByRef) sb.Append("ref ");

                Type pt = p.ParameterType.IsByRef ? p.ParameterType.GetElementType() : p.ParameterType;
                sb.Append(FormatType(pt));
                sb.Append(" ");
                sb.Append(p.Name ?? ("arg" + i));
            }
            sb.Append(")");
            return sb.ToString();
        }

        private static string FormatType(Type t)
        {
            if (t == null) return "<null>";
            if (!t.IsGenericType) return t.FullName ?? t.Name;

            var defName = t.GetGenericTypeDefinition().FullName ?? t.Name;
            int tick = defName.IndexOf('`');
            if (tick >= 0) defName = defName.Substring(0, tick);
            var args = t.GetGenericArguments();
            var sb = new StringBuilder(defName);
            sb.Append("<");
            for (int i = 0; i < args.Length; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append(FormatType(args[i]));
            }
            sb.Append(">");
            return sb.ToString();
        }

        private static void Save(StringBuilder sb)
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Sports Interactive", "Football Manager 26", "FM26FullPlayerProbe");
                Directory.CreateDirectory(dir);
                string file = Path.Combine(dir, "addnodesig_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".txt");
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
