using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using FM.UI;
using FM.Game;
using FM.GamePlugin;
using SI.Core;
using SI.Bindable;
using SI.Bindable.Reference.Core;

namespace FM26FullPlayerProbe
{
    [BepInPlugin("com.schumy12.fm26.fullplayerprobe", "FM26 Full Player Probe", "0.44.0")]
    public sealed class Plugin : BasePlugin
    {
        internal static new BepInEx.Logging.ManualLogSource Log;
        private static ProbeBehaviour _behaviour;
        public override void Load()
        {
            Log = base.Log;
            Log.LogInfo("[FM26FullProbe] Loaded v0.44 PROFILE CLEANUP - select one player row and press F8 once.");
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
        private struct Target
        {
            public string OutputName;
            public string PropertyName;
            public uint Id;
            public bool ResolveReferenceName;
            public Target(string outputName, string propertyName, uint id, bool resolveReferenceName = false)
            {
                OutputName = outputName;
                PropertyName = propertyName;
                Id = id;
                ResolveReferenceName = resolveReferenceName;
            }
        }

        private static readonly Target[] Targets = new Target[]
        {
            new Target("UniqueId", "UniqueId", 1970170212u),
            new Target("Name", "Name", 1851878757u),
            new Target("Surname", "Surname", 843789105u),
            new Target("ShirtName", "ShirtName", 1482779476u),
            new Target("Age", "Age", 825565216u),
            new Target("DateOfBirth", "DateOfBirth", 1348759394u),
            new Target("Height", "Height", 825761824u),
            new Target("Gender", "Gender", 1734700644u),
            new Target("Nationality", "NationalityText", 1851880537u),
            new Target("Nationalities", "NationalitiesText", 1851880532u),
            new Target("CityOfBirth", "CityOfBirth", 1668245090u, true),
            new Target("NationOfBirth", "NationOfBirth", 1349414754u, true),
            new Target("Club", "Club", 825630752u, true),
            new Target("Team", "Team", 1415930221u, true),
            new Target("Footedness", "PlayerFootednessSpeakTo", 1111782216u),
            new Target("CurrentReputation", "PlayerCurrentReputation", 1146252104u),
            new Target("HomeReputation", "PlayerHomeReputation", 1346916944u),
            new Target("WorldReputation", "PlayerWorldReputation", 1347899984u),
            new Target("Personality", "Personality", 1349742196u),
            new Target("IsEuNational", "IsEuNational", 1344292181u),
            new Target("BestPosition", "BestPositionShortString", 1349546835u),
            new Target("NaturalPosition", "NaturalPositionShortString", 1349546834u),
            new Target("Positions", "PositionCombinedStringLong", 2019119186u),
            new Target("CompetentPositions", "CompetentPositionsListLong", 1483174254u),
            new Target("PlayerCurrentAbility", "PlayerCurrentAbility", 1346584898u),
            new Target("PlayerPotentialAbility", "PlayerPotentialAbility", 1347436866u)
        };

        private const uint NamePropertyId = 1851878757u;
        private const float WaitSeconds = 0.50f;

        private BindingSubsystem _bindings;
        private InteropDataHandler _handler;
        private IDataHandler _handlerInterface;
        private GameInteropSubsystem _interop;
        private Bindings.Key _key;
        private Bindings.Node _node;
        private Bindings.Data _data;
        private TypedValue _playerSource;
        private TypedValue _activeSource;
        private TypedValue _pendingReferenceSource;
        private StringBuilder _log;
        private readonly Dictionary<string, string> _values = new Dictionary<string, string>();
        private int _personIndex;
        private long _personData1;
        private int _targetIndex;
        private bool _waiting;
        private bool _channelOpen;
        private bool _nativeNodeAdded;
        private bool _resolvingReferenceName;
        private float _checkAt;

        public ProbeBehaviour(IntPtr ptr) : base(ptr) { }

        private void Update()
        {
            try
            {
                if (!_waiting && _log == null && Keyboard.current != null && Keyboard.current.f8Key.wasPressedThisFrame) StartExport();
                if (_waiting && Time.unscaledTime >= _checkAt) FinishCurrentQuery();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("[FM26FullProbe] Update error: " + ex);
                try { _log?.AppendLine("UPDATE/FATAL: " + ex); } catch { }
                SaveAndReset(false);
            }
        }

        private void StartExport()
        {
            _values.Clear();
            _log = new StringBuilder();
            _log.AppendLine("=== FM26 FULL PLAYER PROBE 0.44 PROFILE CLEANUP ===");
            _log.AppendLine("Tests clean profile strings, textual footedness/reputation fields and nested Name resolution for Nation/City/Club/Team references.");
            _log.AppendLine("Targets=" + Targets.Length + " waitPerQuery=" + WaitSeconds.ToString("0.00") + "s");
            _log.AppendLine();

            var showPerson = FindSelectedShowPerson(_log);
            if (showPerson == null) { _log.AppendLine("RESULT: selected ShowPerson NOT FOUND"); SaveAndReset(false); return; }

            try
            {
                BindingPath path; ActionGroupings groupings; bool multiple;
                var objects = PluginContextMenuContributor.GetContextMenuObjects(showPerson, out path, out groupings, out multiple);
                _log.AppendLine("GetContextMenuObjects count=" + (objects == null ? -1 : objects.Count) + " multiple=" + multiple);
                if (objects == null || objects.Count == 0) { _log.AppendLine("RESULT: no context objects"); SaveAndReset(false); return; }
                for (int i = 0; i < objects.Count; i++)
                {
                    var tv = objects[i];
                    if (_playerSource == null && SafeType(tv) == "FM.UI.PersonReference") _playerSource = tv;
                }
                if (_playerSource == null) { _log.AppendLine("RESULT: no PersonReference source"); SaveAndReset(false); return; }
                var raw = _playerSource.Get();
                var pr = new PersonReference(raw.Pointer);
                _personIndex = pr.m_index;
                _personData1 = pr.Data1;
                _log.AppendLine("REAL PERSON Data1=" + _personData1 + " m_index=" + _personIndex + " combined=" + pr.CombinedIndexAndType + " type=" + pr.Type);
            }
            catch (Exception ex)
            {
                _log.AppendLine("context/source FAIL: " + ex.GetType().Name + " - " + ex.Message);
                SaveAndReset(false); return;
            }

            _bindings = EmbeddedDataHandler.s_bindingSubsystem;
            if (_bindings == null) { _log.AppendLine("RESULT: BindingSubsystem NOT FOUND"); SaveAndReset(false); return; }
            _handler = FindLiveInteropHandler(_log, _bindings);
            if (_handler == null || _handlerInterface == null || _interop == null) { _log.AppendLine("RESULT: Interop handler/subsystem NOT FOUND"); SaveAndReset(false); return; }

            try
            {
                _key = CreateTemporaryNode(_bindings, "__fm26probe_profile_cleanup");
                _log.AppendLine("NODE key=" + _key.m_key + " valid=" + _key.IsValid() + " exists=" + _bindings.Exists(ref _key));
                if (_bindings.m_nodes == null || !_bindings.m_nodes.ContainsKey(_key.m_key)) { _log.AppendLine("RESULT: created key not present in m_nodes"); SaveAndReset(false); return; }
                _node = _bindings.m_nodes[_key.m_key];
                if (_node == null) { _log.AppendLine("RESULT: m_nodes entry is null"); SaveAndReset(false); return; }
                _targetIndex = 0;
                StartCurrentTarget();
            }
            catch (Exception ex)
            {
                _log.AppendLine("START FAIL: " + ex.GetType().Name + " - " + ex.Message);
                SaveAndReset(false);
            }
        }

        private void StartCurrentTarget()
        {
            if (_targetIndex >= Targets.Length)
            {
                _log.AppendLine();
                _log.AppendLine("RESULT: profile cleanup targets completed; writing CSV");
                SaveAndReset(true);
                return;
            }

            var t = Targets[_targetIndex];
            _resolvingReferenceName = false;
            _pendingReferenceSource = null;
            _activeSource = _playerSource;
            _log.AppendLine();
            _log.AppendLine("TARGET [" + _targetIndex + "/" + Targets.Length + "] " + t.OutputName + " <= " + t.PropertyName);
            StartQuery(_activeSource, t.PropertyName, t.Id);
        }

        private void StartQuery(TypedValue source, string propertyName, uint propertyId)
        {
            var propId = new PropertyID(propertyId);
            _node.m_propID = propId;
            _data = _bindings.GetNewData();
            if (_data == null) throw new Exception("GetNewData returned null");
            _bindings.SetTargetData(_data, _node);
            var dataKey = _node.m_dataKey;
            var parentKey = _node.m_parent == null ? default(Bindings.Key) : _node.m_parent.m_key;

            dynamic runtimeInterop = _interop;
            runtimeInterop.AddNode(_key, parentKey, propId);
            _nativeNodeAdded = true;
            _interop.AddData(dataKey);
            _interop.SetTarget(_key, dataKey);
            _data.handler = _handlerInterface;
            _data.opener = _key;

            var property = new Bindings.Property(propertyName, propId);
            var contexts = new Il2CppSystem.Collections.Generic.List<string>();
            bool canHandle = _handler.CanHandle(source, property, contexts);
            _log.AppendLine("  OPEN property=" + propertyName + " sourceType=" + SafeType(source) + " canHandle=" + canHandle);
            _handler.OpenChannel(source, property, _key);
            _channelOpen = true;
            _waiting = true;
            _checkAt = Time.unscaledTime + WaitSeconds;
        }

        private void FinishCurrentQuery()
        {
            _waiting = false;
            var t = Targets[_targetIndex];
            TypedValue tv = null;
            bool isSet = false;
            try
            {
                isSet = _data != null && _data.IsSet;
                tv = _data == null ? null : _data.Value;
                _log.AppendLine("  RAW isSet=" + isSet + " type=" + SafeType(tv) + " value='" + SafeText(tv) + "'");
            }
            catch (Exception ex)
            {
                _log.AppendLine("  READ FAIL: " + ex.GetType().Name + " - " + ex.Message);
            }

            if (_resolvingReferenceName)
            {
                string resolved = isSet && tv != null ? CleanUiString(SafeText(tv)) : "";
                _values[t.OutputName] = resolved;
                _log.AppendLine("  RESOLVED " + t.OutputName + "='" + resolved + "'");
                CleanupCurrentGraph();
                _resolvingReferenceName = false;
                _pendingReferenceSource = null;
                _targetIndex++;
                StartCurrentTarget();
                return;
            }

            if (t.ResolveReferenceName && isSet && tv != null && SafeType(tv).EndsWith("Reference"))
            {
                _pendingReferenceSource = tv;
                _log.AppendLine("  REFERENCE detected; resolving nested Name...");
                CleanupCurrentGraph();
                _resolvingReferenceName = true;
                _activeSource = _pendingReferenceSource;
                try { StartQuery(_activeSource, "Name", NamePropertyId); }
                catch (Exception ex)
                {
                    _log.AppendLine("  NESTED NAME FAIL: " + ex.GetType().Name + " - " + ex.Message);
                    _values[t.OutputName] = "";
                    _resolvingReferenceName = false;
                    _targetIndex++;
                    StartCurrentTarget();
                }
                return;
            }

            string finalValue = "";
            try
            {
                if (!isSet || tv == null) finalValue = "";
                else if (SafeType(tv) == "SI.Bindable.DynamicReference")
                {
                    var dyn = VisualFunctionLibrary.GetDynamicReference(tv);
                    var inner = VisualFunctionLibrary.GetPropertyValue(dyn);
                    finalValue = CleanUiString(SafeText(inner));
                }
                else finalValue = CleanUiString(SafeText(tv));
            }
            catch (Exception ex)
            {
                _log.AppendLine("  VALUE CONVERT FAIL: " + ex.GetType().Name + " - " + ex.Message);
                finalValue = "";
            }

            _values[t.OutputName] = finalValue;
            _log.AppendLine("  CLEAN " + t.OutputName + "='" + finalValue + "'");
            CleanupCurrentGraph();
            _targetIndex++;
            StartCurrentTarget();
        }

        private void CleanupCurrentGraph()
        {
            CloseChannel();
            if (_nativeNodeAdded && _interop != null)
            {
                try { _interop.RemoveNode(_key); }
                catch (Exception ex) { try { _log?.AppendLine("  native RemoveNode FAIL: " + ex.GetType().Name + " - " + ex.Message); } catch { } }
                _nativeNodeAdded = false;
            }
            _data = null;
        }

        private InteropDataHandler FindLiveInteropHandler(StringBuilder sb, BindingSubsystem bindings)
        {
            try
            {
                var registry = bindings.m_handlers;
                if (registry == null) return null;
                foreach (var pair in registry)
                {
                    string n = ""; try { n = pair.Key == null ? "" : pair.Key.FullName; } catch { }
                    if (n == null || !n.Contains("InteropReference")) continue;
                    var list = pair.Value; if (list == null) continue;
                    for (int i = 0; i < list.Count; i++)
                    {
                        var h = list[i]; if (h == null) continue;
                        try
                        {
                            var concrete = new InteropDataHandler(h.Pointer);
                            if (concrete.m_interop != null)
                            {
                                _handlerInterface = h;
                                _interop = new GameInteropSubsystem(concrete.m_interop.Pointer);
                                sb.AppendLine("InteropDataHandler ptr=0x" + concrete.Pointer.ToString("X") + " interop=0x" + _interop.Pointer.ToString("X"));
                                return concrete;
                            }
                        }
                        catch { }
                    }
                }
            }
            catch (Exception ex) { sb.AppendLine("Find handler FAIL: " + ex.GetType().Name + " - " + ex.Message); }
            return null;
        }

        private static string SafeType(TypedValue tv) { try { return tv == null || tv.DataType == null ? "<null>" : tv.DataType.FullName; } catch { return "<failed>"; } }
        private static string SafeText(TypedValue tv) { try { return tv == null ? "" : (tv.AsString() ?? ""); } catch { return ""; } }

        private static string CleanUiString(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            int marker = s.LastIndexOf('\u0002');
            if (marker >= 0 && marker + 1 < s.Length) s = s.Substring(marker + 1);
            var sb = new StringBuilder();
            for (int i = 0; i < s.Length; i++) if (!char.IsControl(s[i])) sb.Append(s[i]);
            return sb.ToString().Trim();
        }

        private static VisualElement FindSelectedShowPerson(StringBuilder sb)
        {
            try
            {
                var docs = Resources.FindObjectsOfTypeAll<UIDocument>();
                sb.AppendLine("UIDocument count=" + (docs == null ? 0 : docs.Length));
                if (docs == null) return null;
                for (int i = 0; i < docs.Length; i++)
                {
                    var doc = docs[i]; if (doc == null) continue;
                    VisualElement root = null; try { root = doc.rootVisualElement; } catch { }
                    var found = FindSelectedRecursive(root);
                    if (found != null) { sb.AppendLine("selected element found in UIDocument[" + i + "]"); return found; }
                }
            }
            catch (Exception ex) { sb.AppendLine("FindSelected FAIL: " + ex.GetType().Name + " - " + ex.Message); }
            return null;
        }

        private static VisualElement FindSelectedRecursive(VisualElement ve)
        {
            if (ve == null) return null;
            try { if (ve.ClassListContains("virtualised-list__item--selected")) { var show = FindNamedRecursive(ve, "ShowPerson"); if (show != null) return show; } } catch { }
            int count = 0; try { count = ve.childCount; } catch { }
            for (int i = 0; i < count; i++) { VisualElement child = null; try { child = ve[i]; } catch { } var found = FindSelectedRecursive(child); if (found != null) return found; }
            return null;
        }

        private static VisualElement FindNamedRecursive(VisualElement ve, string name)
        {
            if (ve == null) return null;
            try { if (ve.name == name) return ve; } catch { }
            int count = 0; try { count = ve.childCount; } catch { }
            for (int i = 0; i < count; i++) { VisualElement child = null; try { child = ve[i]; } catch { } var found = FindNamedRecursive(child, name); if (found != null) return found; }
            return null;
        }

        private static unsafe Bindings.Key CreateTemporaryNode(BindingSubsystem bindings, string name)
        {
            fixed (char* p = name) { var span = new Il2CppSystem.ReadOnlySpan<char>((void*)p, name.Length); return bindings.Create(ref span, Bindings.NodeFlags.Temporary); }
        }

        private void CloseChannel()
        {
            if (!_channelOpen || _handler == null) return;
            try { _handler.CloseChannel(_key); } catch (Exception ex) { try { _log?.AppendLine("CLOSE FAIL: " + ex.GetType().Name + " - " + ex.Message); } catch { } }
            _channelOpen = false;
        }

        private static string Csv(string s)
        {
            if (s == null) s = "";
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }

        private void SaveAndReset(bool writeCsv)
        {
            CleanupCurrentGraph();
            _waiting = false;
            if (_log != null)
            {
                try
                {
                    string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Sports Interactive", "Football Manager 26", "FM26FullPlayerProbe");
                    Directory.CreateDirectory(dir);
                    string stamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
                    string logFile = Path.Combine(dir, "profilecleanup_" + stamp + ".txt");
                    File.WriteAllText(logFile, _log.ToString(), Encoding.UTF8);
                    Plugin.Log.LogInfo("[FM26FullProbe] Saved log: " + logFile);
                    if (writeCsv)
                    {
                        var csv = new StringBuilder();
                        csv.Append("PersonIndex,PersonData1");
                        for (int i = 0; i < Targets.Length; i++) csv.Append("," + Csv(Targets[i].OutputName));
                        csv.AppendLine();
                        csv.Append(_personIndex + "," + _personData1);
                        for (int i = 0; i < Targets.Length; i++)
                        {
                            string v; _values.TryGetValue(Targets[i].OutputName, out v);
                            csv.Append("," + Csv(v));
                        }
                        csv.AppendLine();
                        string csvFile = Path.Combine(dir, "profilecleanup_" + stamp + ".csv");
                        File.WriteAllText(csvFile, csv.ToString(), new UTF8Encoding(true));
                        Plugin.Log.LogInfo("[FM26FullProbe] Saved CSV: " + csvFile);
                    }
                }
                catch (Exception ex) { Plugin.Log.LogError("[FM26FullProbe] Save failed: " + ex); }
            }
            _data = null; _node = null; _playerSource = null; _activeSource = null; _pendingReferenceSource = null;
            _handlerInterface = null; _handler = null; _interop = null; _bindings = null; _log = null;
            _resolvingReferenceName = false;
        }
    }
}
