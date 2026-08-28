using System;
using System.IO;
using System.Reflection;
using System.Text;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using UnityEngine;
using UnityEngine.InputSystem;
using FM.UI;
using SI.Bindable.Reference.Core;

namespace FM26FullPlayerProbe
{
    [BepInPlugin("com.schumy12.fm26.fullplayerprobe", "FM26 Full Player Probe", "0.18.0")]
    public sealed class Plugin : BasePlugin
    {
        internal static new BepInEx.Logging.ManualLogSource Log;
        private static ProbeBehaviour _behaviour;

        public override void Load()
        {
            Log = base.Log;
            Log.LogInfo("[FM26FullProbe] Loaded v0.18 PERSON PROPERTY SCHEMA - press F8 after loading a save.");
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
            sb.AppendLine("=== FM26 FULL PLAYER PROBE 0.18 PERSON PROPERTY SCHEMA ===");
            sb.AppendLine("Goal: determine whether PersonReference.UID is a reference-type identifier or an actual readable person property.");
            sb.AppendLine("PersonReference.UID=" + PersonReference.UID);
            sb.AppendLine("PersonReference.Identifier=" + Safe(() => PersonReference.Identifier.ToString()));
            sb.AppendLine();

            sb.AppendLine("=== UID SCHEMA TEST ===");
            try { sb.AppendLine("AcceptsPropertyInternal(UID)=" + PersonReference.AcceptsPropertyInternal(PersonReference.UID)); }
            catch (Exception ex) { sb.AppendLine("AcceptsPropertyInternal(UID) failed: " + ex.GetType().Name + " - " + ex.Message); }

            try { sb.AppendLine("GetPropertyTypeInternal(UID)=" + PersonReference.GetPropertyTypeInternal(PersonReference.UID)); }
            catch (Exception ex) { sb.AppendLine("GetPropertyTypeInternal(UID) failed: " + ex.GetType().Name + " - " + ex.Message); }

            try { sb.AppendLine("GetPropertyDescriptionInternal(UID)='" + (PersonReference.GetPropertyDescriptionInternal(PersonReference.UID) ?? "") + "'"); }
            catch (Exception ex) { sb.AppendLine("GetPropertyDescriptionInternal(UID) failed: " + ex.GetType().Name + " - " + ex.Message); }

            try { sb.AppendLine("GetPropertyCountInternal()=" + PersonReference.GetPropertyCountInternal()); }
            catch (Exception ex) { sb.AppendLine("GetPropertyCountInternal failed: " + ex.GetType().Name + " - " + ex.Message); }

            sb.AppendLine();
            sb.AppendLine("=== PERSONREFERENCE PROPERTY LIST ===");
            DumpPropertyList(sb);

            sb.AppendLine();
            sb.AppendLine("=== REFERENCE RELATION TESTS ===");
            try { sb.AppendLine("PersonReference.DerivesFromInternal(PersonReference.UID)=" + PersonReference.DerivesFromInternal(PersonReference.UID)); }
            catch (Exception ex) { sb.AppendLine("PersonReference.DerivesFromInternal(UID) failed: " + ex.GetType().Name + " - " + ex.Message); }

            try
            {
                uint playerUid = GetReferenceUidFromIdentifier(IPlayerReference.Identifier.ToString());
                sb.AppendLine("IPlayerReference.Identifier=" + IPlayerReference.Identifier.ToString());
                sb.AppendLine("IPlayerReference parsed UID=" + playerUid);
                if (playerUid != 0)
                    sb.AppendLine("PersonReference.DerivesFromInternal(IPlayerReference UID)=" + PersonReference.DerivesFromInternal(playerUid));
            }
            catch (Exception ex)
            {
                sb.AppendLine("IPlayerReference relation test failed: " + ex.GetType().Name + " - " + ex.Message);
            }

            sb.AppendLine();
            sb.AppendLine("=== PROPERTYID MANAGED METADATA (NO GETTERS INVOKED) ===");
            DumpTypeMetadata(sb, typeof(PropertyID));
            DumpTypeMetadata(sb, typeof(ReferenceID));

            Save(sb);
        }

        private static void DumpPropertyList(StringBuilder sb)
        {
            try
            {
                var props = new Il2CppSystem.Collections.Generic.List<PropertyID>();
                PersonReference.GetPropertiesInternal(props);
                sb.AppendLine("GetPropertiesInternal list count=" + props.Count);

                for (int i = 0; i < props.Count; i++)
                {
                    PropertyID pid = props[i];
                    string text = "?";
                    try { text = pid.ToString(); } catch (Exception ex) { text = "<ToString failed: " + ex.GetType().Name + ">"; }
                    sb.AppendLine("PROPERTY[" + i + "] text='" + text + "'");
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine("GetPropertiesInternal failed: " + ex.GetType().Name + " - " + ex.Message);
            }
        }

        private static uint GetReferenceUidFromIdentifier(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            int colon = s.LastIndexOf(':');
            if (colon < 0 || colon + 1 >= s.Length) return 0;
            uint value;
            return uint.TryParse(s.Substring(colon + 1), out value) ? value : 0;
        }

        private static string Safe(Func<string> f)
        {
            try { return f(); }
            catch (Exception ex) { return "<" + ex.GetType().Name + ": " + ex.Message + ">"; }
        }

        private static void DumpTypeMetadata(StringBuilder sb, Type t)
        {
            if (t == null) return;
            sb.AppendLine("TYPE " + (t.FullName ?? t.Name));
            try { sb.AppendLine("  BaseType=" + (t.BaseType == null ? "<null>" : (t.BaseType.FullName ?? t.BaseType.Name))); } catch { }

            var flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            try
            {
                foreach (var p in t.GetProperties(flags))
                    sb.AppendLine("  PROP " + (IsStatic(p) ? "static " : "") + p.Name + " : " + SafeTypeName(p.PropertyType));
            }
            catch (Exception ex) { sb.AppendLine("  properties failed: " + ex.GetType().Name); }

            try
            {
                foreach (var f in t.GetFields(flags))
                    sb.AppendLine("  FIELD " + (f.IsStatic ? "static " : "") + f.Name + " : " + SafeTypeName(f.FieldType));
            }
            catch (Exception ex) { sb.AppendLine("  fields failed: " + ex.GetType().Name); }

            try
            {
                foreach (var m in t.GetMethods(flags))
                    sb.AppendLine("  METHOD " + (m.IsStatic ? "static " : "") + SafeMemberString(m));
            }
            catch (Exception ex) { sb.AppendLine("  methods failed: " + ex.GetType().Name); }
        }

        private static bool IsStatic(PropertyInfo p)
        {
            try
            {
                var g = p.GetGetMethod(true);
                if (g != null) return g.IsStatic;
                var s = p.GetSetMethod(true);
                return s != null && s.IsStatic;
            }
            catch { return false; }
        }

        private static string SafeTypeName(Type t)
        {
            try { return t == null ? "?" : (t.FullName ?? t.Name); }
            catch { return "?"; }
        }

        private static string SafeMemberString(MethodBase m)
        {
            try { return m == null ? "?" : m.ToString(); }
            catch { return m == null ? "?" : m.Name; }
        }

        private static void Save(StringBuilder sb)
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Sports Interactive", "Football Manager 26", "FM26FullPlayerProbe");
                Directory.CreateDirectory(dir);
                string file = Path.Combine(dir, "propertyschema_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".txt");
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
