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
    [BepInPlugin("com.schumy12.fm26.fullplayerprobe", "FM26 Full Player Probe", "0.42.1")]
    public sealed class Plugin : BasePlugin
    {
        internal static new BepInEx.Logging.ManualLogSource Log;
        private static ProbeBehaviour _behaviour;
        public override void Load()
        {
            Log = base.Log;
            Log.LogInfo("[FM26FullProbe] Loaded v0.42.1 SELECTED PLAYER TRUE CSV - select one player row and press F8 once.");
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
            public string Name;
            public uint Id;
            public Target(string name, uint id) { Name = name; Id = id; }
        }

        private static readonly Target[] Targets = new Target[]
        {
            new Target("PlayerCurrentAbility", 1346584898u),
            new Target("PlayerPotentialAbility", 1347436866u),

            new Target("AttributeAdaptability", 1348559969u),
            new Target("AttributeAmbition", 1348562274u),
            new Target("AttributeControversy", 1348695673u),
            new Target("AttributeLoyalty", 1349283705u),
            new Target("AttributePressure", 1349546597u),
            new Target("AttributeProfessionalism", 1349546607u),
            new Target("AttributeSportsmanship", 1349742703u),
            new Target("AttributeTemperament", 1349805421u),
            new Target("Consistency", 1346588494u),
            new Target("ImportantMatches", 1349086576u),
            new Target("InjuryProneness", 1349087346u),
            new Target("Versatility", 1349936498u),

            new Target("Acceleration", 892805152u),
            new Target("AerialReach", 926232624u),
            new Target("Aggression", 875765792u),
            new Target("Agility", 892870688u),
            new Target("Anticipation", 875831328u),
            new Target("Balance", 892936224u),
            new Target("Bravery", 875896864u),
            new Target("CommandOfArea", 909516833u),
            new Target("Communication", 909582369u),
            new Target("Composure", 875962400u),
            new Target("Concentration", 876027936u),
            new Target("Corners", 842604576u),
            new Target("Crossing", 858791968u),
            new Target("Decisions", 876093472u),
            new Target("Determination", 876159008u),
            new Target("Dribbling", 858857504u),
            new Target("Eccentricity", 909647905u),
            new Target("Finishing", 858923040u),
            new Target("FirstTouch", 858988576u),
            new Target("Flair", 892346400u),
            new Target("FreeKicks", 859054112u),
            new Target("Handling", 909713441u),
            new Target("Heading", 859119648u),
            new Target("JumpingReach", 909123616u),
            new Target("Kicking", 925900832u),
            new Target("Leadership", 892411936u),
            new Target("LongShots", 859185184u),
            new Target("LongThrows", 859250720u),
            new Target("Marking", 859316256u),
            new Target("Movement", 892477472u),
            new Target("NaturalFitness", 909189152u),
            new Target("OneOnOnes", 926167088u),
            new Target("Pace", 909254688u),
            new Target("Passing", 859381792u),
            new Target("PenaltyTaking", 875569184u),
            new Target("Positioning", 892543008u),
            new Target("Reflexes", 925966369u),
            new Target("RushingOut", 925970480u),
            new Target("Stamina", 909320224u),
            new Target("Strength", 909385760u),
            new Target("Tackling", 875634720u),
            new Target("Teamwork", 892608544u),
            new Target("Technique", 875700256u),
            new Target("TendencyToPunch", 926036016u),
            new Target("Throwing", 926101552u),
            new Target("Vision", 892674080u),
            new Target("WorkRate", 892739616u)
        };

        private const float WaitSeconds = 0.50f;
        private BindingSubsystem _bindings;
        private InteropDataHandler _handler;
        private IDataHandler _handlerInterface;
        private GameInteropSubsystem _interop;
        private Bindings.Key _key;
        private Bindings.Node _node;
        private Bindings.Data _data;
        private TypedValue _source;
        private StringBuilder _log;
        private readonly Dictionary<string, string> _values = new Dictionary<string, string>();
        private int _personIndex;
        private long _personData1;
        private int _targetIndex;
        private bool _waiting;
        private bool _channelOpen;
        private bool _nativeNodeAdded;
        private float _checkAt;

        public ProbeBehaviour(IntPtr ptr) : base(ptr) { }

        private void Update()
        {
            try
            {
                if (!_waiting && _log == null && Keyboard.current != null && Keyboard.current.f8Key.wasPressedThisFrame) StartExport();
                if (_waiting && Time.unscaledTime >= _checkAt) FinishCurrentTarget();
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
            _log.AppendLine("=== FM26 FULL PLAYER PROBE 0.42.1 SELECTED PLAYER TRUE CSV ===");
            _log.AppendLine("Exports CA, PA, hidden personality attributes and all standard player attributes from the real backend PersonReference.");
            _log.AppendLine("Targets=" + Targets.Length + " waitPerTarget=" + WaitSeconds.ToString("0.00") + "s");
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
                    string type = SafeType(tv);
                    if (_source == null && type == "FM.UI.PersonReference") _source = tv;
                }
                if (_source == null) { _log.AppendLine("RESULT: no PersonReference source"); SaveAndReset(false); return; }
                var raw = _source.Get();
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
                _key = CreateTemporaryNode(_bindings, "__fm26probe_selected_player_true_csv");
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
                _log.AppendLine("RESULT: all targets completed; writing CSV");
                SaveAndReset(true);
                return;
            }

            var t = Targets[_targetIndex];
            var propId = new PropertyID(t.Id);
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

            var property = new Bindings.Property(t.Name, propId);
            var contexts = new Il2CppSystem.Collections.Generic.List<string>();
            bool accepts = PersonReference.AcceptsPropertyInternal(t.Id);
            bool canHandle = _handler.CanHandle(_source, property, contexts);
            _log.AppendLine("OPEN [" + _targetIndex + "/" + Targets.Length + "] " + t.Name + " accepts=" + accepts + " canHandle=" + canHandle);
            _handler.OpenChannel(_source, property, _key);
            _channelOpen = true;
            _waiting = true;
            _checkAt = Time.unscaledTime + WaitSeconds;
        }

        private void FinishCurrentTarget()
        {
            _waiting = false;
            var t = Targets[_targetIndex];
            string finalValue = "";
            try
            {
                var tv = _data == null ? null : _data.Value;
                if (_data == null || !_data.IsSet || tv == null)
                {
                    finalValue = "";
                    _log.AppendLine("  VALUE " + t.Name + "=<unset>");
                }
                else if (SafeType(tv) == "SI.Bindable.DynamicReference")
                {
                    var dyn = VisualFunctionLibrary.GetDynamicReference(tv);
                    var inner = VisualFunctionLibrary.GetPropertyValue(dyn);
                    finalValue = SafeText(inner);
                    _log.AppendLine("  VALUE " + t.Name + "='" + finalValue + "' (unwrapped " + SafeType(inner) + ")");
                }
                else
                {
                    finalValue = SafeText(tv);
                    _log.AppendLine("  VALUE " + t.Name + "='" + finalValue + "' (" + SafeType(tv) + ")");
                }
            }
            catch (Exception ex)
            {
                _log.AppendLine("  READ FAIL " + t.Name + ": " + ex.GetType().Name + " - " + ex.Message);
                finalValue = "";
            }
            _values[t.Name] = finalValue;

            CloseChannel();
            if (_nativeNodeAdded && _interop != null)
            {
                try { _interop.RemoveNode(_key); }
                catch (Exception ex) { _log.AppendLine("native RemoveNode FAIL: " + ex.GetType().Name + " - " + ex.Message); SaveAndReset(false); return; }
                _nativeNodeAdded = false;
            }

            _data = null;
            _targetIndex++;
            try { StartCurrentTarget(); }
            catch (Exception ex)
            {
                _log.AppendLine("NEXT TARGET FAIL: " + ex.GetType().Name + " - " + ex.Message);
                SaveAndReset(false);
            }
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
            CloseChannel();
            if (_nativeNodeAdded && _interop != null)
            {
                try { _interop.RemoveNode(_key); } catch { }
                _nativeNodeAdded = false;
            }
            _waiting = false;

            if (_log != null)
            {
                try
                {
                    string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Sports Interactive", "Football Manager 26", "FM26FullPlayerProbe");
                    Directory.CreateDirectory(dir);
                    string stamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
                    string logFile = Path.Combine(dir, "selectedtrue_" + stamp + ".txt");
                    File.WriteAllText(logFile, _log.ToString(), Encoding.UTF8);
                    Plugin.Log.LogInfo("[FM26FullProbe] Saved log: " + logFile);

                    if (writeCsv)
                    {
                        var csv = new StringBuilder();
                        csv.Append("PersonIndex,PersonData1");
                        for (int i = 0; i < Targets.Length; i++) csv.Append("," + Csv(Targets[i].Name));
                        csv.AppendLine();
                        csv.Append(_personIndex + "," + _personData1);
                        for (int i = 0; i < Targets.Length; i++)
                        {
                            string v;
                            _values.TryGetValue(Targets[i].Name, out v);
                            csv.Append("," + Csv(v));
                        }
                        csv.AppendLine();
                        string csvFile = Path.Combine(dir, "selectedtrue_" + stamp + ".csv");
                        File.WriteAllText(csvFile, csv.ToString(), new UTF8Encoding(true));
                        Plugin.Log.LogInfo("[FM26FullProbe] Saved CSV: " + csvFile);
                    }
                }
                catch (Exception ex) { Plugin.Log.LogError("[FM26FullProbe] Save failed: " + ex); }
            }

            _data = null; _node = null; _source = null; _handlerInterface = null; _handler = null; _interop = null; _bindings = null; _log = null;
        }
    }
}
