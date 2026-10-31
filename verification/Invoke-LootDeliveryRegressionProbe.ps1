[CmdletBinding()]
param(
 [Parameter(Mandatory=$true)][string]$SourceRoot,
 [Parameter(Mandatory=$true)][string]$CompilerPath,
 [Parameter(Mandatory=$true)][string[]]$ReferencePaths,
 [Parameter(Mandatory=$true)][string]$OutputDirectory,
 [Parameter(Mandatory=$true)][string]$InputHashesJson
)
$ErrorActionPreference='Stop'
$root=(Resolve-Path -LiteralPath $SourceRoot).ProviderPath
$compiler=(Resolve-Path -LiteralPath $CompilerPath).ProviderPath
$refs=@($ReferencePaths | ForEach-Object { (Resolve-Path -LiteralPath $_).ProviderPath })
if (!$refs.Count) { throw 'ReferencePaths must be explicit.' }
if (Test-Path -LiteralPath $OutputDirectory) { throw 'OutputDirectory must not already exist.' }
$names=@('GVK_KOTH/Zones/LootDelivery.cs','GVK_KOTH/Zones/ZoneManager.cs','GVK_KOTH.Core/KothState.cs','GVK_KOTH/Commands/KothAdminCommands.cs','GVK_KOTH/Zones/NpcZoneTracker.cs')
$paths=@{}; $hashes=[ordered]@{}; $sources=@{}
foreach($name in $names) {
 $p=(Resolve-Path -LiteralPath (Join-Path $root $name)).ProviderPath
 $paths[$name]=$p; $hashes[$name]=(Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash.ToLowerInvariant()
 $sources[$name]=[IO.File]::ReadAllText($p)
}
$pinsPath=(Resolve-Path -LiteralPath $InputHashesJson).ProviderPath
$pins=Get-Content -LiteralPath $pinsPath -Raw | ConvertFrom-Json
if ($null -eq $pins -or @($pins.PSObject.Properties).Count -ne $names.Count) { throw 'InputHashesJson must pin exactly the five source paths.' }
foreach($name in $names) {
 $pin=$pins.PSObject.Properties[$name]
 if ($null -eq $pin -or $pin.Value -isnot [string] -or $pin.Value -notmatch '^[0-9a-fA-F]{64}$' -or $pin.Value -ne $hashes[$name]) { throw "Missing or mismatched source pin: $name" }
}
function Slice([string]$s,[string]$start,[string]$end) {
 $a=$s.IndexOf($start,[StringComparison]::Ordinal)
 if($a -lt 0 -or $s.IndexOf($start,$a+$start.Length,[StringComparison]::Ordinal) -ge 0) { throw "Missing or ambiguous source anchor: $start" }
 $b=$s.IndexOf($end,$a+$start.Length,[StringComparison]::Ordinal)
 if($b -le $a) { throw "Missing end anchor: $end" }
 return $s.Substring($a,$b-$a)
}
$loot=$sources[$names[0]]
$declarations=Slice $loot '    public enum LootDeliveryStatus' '    /// <summary>'
$bundle=Slice $loot '        private sealed class BundleItem' '        /// <summary>'
# Drop only the public container lookup method; retain the production bundle method verbatim.
$public=Slice $bundle '        // Presence check only;' '        // This method and inventory mutations'
$bundle=$bundle.Replace($public,'')
$state=Slice $sources[$names[2]] '    public sealed class LootReconciliationSave' '    public sealed class NpcZoneSave'
$prefix=@'
using System;
using System.Collections.Generic;
using System.Linq;
using GVK.Koth.Core;
using GVK.Koth.Core.Config;
using Sandbox.Definitions;
using Sandbox.Game.Entities.Inventory;
using VRage;
using VRage.Game;
using VRage.ObjectBuilders;
namespace VRageMath { public struct Vector3D {} }
namespace VRage {
 public struct MyFixedPoint {
  public long RawValue;
  public static explicit operator MyFixedPoint(float x) { return new MyFixedPoint { RawValue=(long)(x*1000000) }; }
  public static bool operator <(MyFixedPoint a,MyFixedPoint b)=>a.RawValue<b.RawValue;
  public static bool operator >(MyFixedPoint a,MyFixedPoint b)=>a.RawValue>b.RawValue;
  public static bool operator ==(MyFixedPoint a,MyFixedPoint b)=>a.RawValue==b.RawValue;
  public static bool operator !=(MyFixedPoint a,MyFixedPoint b)=>a.RawValue!=b.RawValue;
  public override bool Equals(object o)=>o is MyFixedPoint && ((MyFixedPoint)o).RawValue==RawValue;
  public override int GetHashCode()=>RawValue.GetHashCode();
 }
}
namespace VRage.ObjectBuilders {
 public struct MyObjectBuilderType {
  public string Name;
  public static bool TryParse(string s,out MyObjectBuilderType t) { t=new MyObjectBuilderType{Name=s}; return s=="MyObjectBuilder_Component"; }
 }
 public static class MyObjectBuilderSerializer {
  public static object CreateNewObject(VRage.Game.MyDefinitionId id) { return new VRage.Game.MyObjectBuilder_PhysicalObject { Id=id }; }
 }
}
namespace VRage.Game {
 public struct MyDefinitionId {
  public string Key; public MyDefinitionId(VRage.ObjectBuilders.MyObjectBuilderType t,string s) { Key=t.Name+"/"+s; }
  public static bool operator ==(MyDefinitionId a,MyDefinitionId b)=>a.Key==b.Key;
  public static bool operator !=(MyDefinitionId a,MyDefinitionId b)=>a.Key!=b.Key;
  public override bool Equals(object o)=>o is MyDefinitionId && this==(MyDefinitionId)o;
  public override int GetHashCode()=>Key.GetHashCode();
 }
 public class MyObjectBuilder_PhysicalObject {
  public MyDefinitionId Id; public MyDefinitionId GetObjectId()=>Id;
  public bool CanStack(MyObjectBuilder_PhysicalObject other)=>Id==other.Id;
 }
}
namespace VRage.Game.ModAPI { public interface IMyInventory { bool CanPutItems {get;} } }
namespace Sandbox.Engine.Utils { public static class MyFakes { public static bool ENABLE_COMPONENT_BLOCKS; } }
namespace Sandbox.Definitions {
 public class MyDefinitionManager {
  public static MyDefinitionManager Static=new MyDefinitionManager();
  public bool TryGetPhysicalItemDefinition(MyDefinitionId id,out object d) { d=id.Key.EndsWith("/Missing")?null:new object(); return d!=null; }
  public readonly HashSet<MyDefinitionId> ComponentBlocks=new HashSet<MyDefinitionId>();
  public object TryGetComponentBlockDefinition(MyDefinitionId id)=>ComponentBlocks.Contains(id) ? new object() : null;
 }
}
namespace GVK.Koth.Core.Config {
 public class LootItem { public string TypeId,SubtypeId; public int Amount; }
 public class ZoneProfile { public List<LootItem> Loot=new List<LootItem>(); public ManagerProbe.ZoneMode Mode=ManagerProbe.ZoneMode.Other; public string Activation="test",Name="profile"; public bool Enabled=true; public int PointsPerCompletion; public long CreditsPerPoint; }
}
namespace Sandbox.Game.Entities.Inventory {
 public class MyInventoryItemAdapter {
  public float Volume=1,Mass=1; public MyFixedPoint MaxStackAmount=new MyFixedPoint{RawValue=100000000};
  public bool TryAdapt(MyDefinitionId id)=>true;
 }
 public class Stack { public uint ItemId; public MyObjectBuilder_PhysicalObject Content; public MyFixedPoint Amount; }
 public class MyInventory : VRage.Game.ModAPI.IMyInventory {
  public bool CanPutItems {get;set;}=true; public bool IsConstrained=true;
  public MyFixedPoint CurrentVolume,CurrentMass,MaxVolume=F(100),MaxMass=F(100); public int MaxItemCount=100;
  public int Adds,Removes,Reads; public string Mode="normal"; public bool Unknown; uint next=1;
  readonly List<Stack> stacks=new List<Stack>();
  public static MyFixedPoint F(long n)=>new MyFixedPoint{RawValue=n*1000000};
  public List<Stack> GetItems()=>stacks.ToList();
  public bool CanItemsBeAdded(MyFixedPoint n,MyDefinitionId id)=>n.RawValue<=MaxVolume.RawValue && n.RawValue<=MaxMass.RawValue;
  public MyFixedPoint ComputeAmountThatFits(MyDefinitionId id)=>F(100);
  public MyFixedPoint GetItemAmount(MyDefinitionId id) { Reads++; if(Unknown && Adds>0) throw new Exception("observation"); return new MyFixedPoint{RawValue=stacks.Where(x=>x.Content.Id==id).Sum(x=>x.Amount.RawValue)}; }
  public void Seed(MyDefinitionId id,long raw) { stacks.Add(new Stack{ItemId=next++,Content=new MyObjectBuilder_PhysicalObject{Id=id},Amount=new MyFixedPoint{RawValue=raw}}); }
  public bool AddItems(MyFixedPoint n,MyObjectBuilder_PhysicalObject b) {
   Adds++; if(Mode=="silent") return true;
   long raw=Mode=="partial" || Mode=="throw" || Mode=="rollbackfail" ? n.RawValue/2:n.RawValue;
   var s=stacks.FirstOrDefault(x=>x.Content.Id==b.Id); if(s==null) Seed(b.Id,raw); else s.Amount=new MyFixedPoint{RawValue=s.Amount.RawValue+raw};
   if(Mode=="throw") throw new Exception("insertion"); return true;
  }
  public void RemoveItems(uint id,MyFixedPoint n,bool a,bool b) { Removes++; if(Mode=="rollbackfail") return; var s=stacks.Single(x=>x.ItemId==id); s.Amount=new MyFixedPoint{RawValue=s.Amount.RawValue-n.RawValue}; if(s.Amount.RawValue==0) stacks.Remove(s); }
 }
}
namespace GVK.Koth.Core {
 public class ZoneSave { public string Name=""; public long PendingLootDrops; public LootReconciliationSave LootReconciliation; public LootPositionSave LootPosition; }
 public class LootPositionSave { public double X,Y,Z; }
'@
$middle="`n}`nnamespace GVK.Koth.Zones {`n"+$declarations+"`n public static class LootDelivery {`n"+$bundle+"`n }`n}`n"
$runner=@'
namespace Probe {
 using GVK.Koth.Zones; using GVK.Koth.Core.Config; using Sandbox.Game.Entities.Inventory;
 class Program {
  static int pass,fail;
  public static void Check(string n,bool ok) { Console.WriteLine((ok?"PASS ":"FAIL ")+n); if(ok) pass++; else fail++; }
  static LootItem Item(string sub,int amount=1)=>new LootItem{TypeId="Component",SubtypeId=sub,Amount=amount};
  static ZoneProfile Zone(params LootItem[] items)=>new ZoneProfile{Loot=items.ToList()};
  static MyDefinitionId Id(string sub) { MyObjectBuilderType t; MyObjectBuilderType.TryParse("MyObjectBuilder_Component",out t); return new MyDefinitionId(t,sub); }
  static LootDeliveryResult Run(MyInventory i,params LootItem[] items)=>LootDelivery.TryDeliverBundle(Zone(items),i,42);
  static int Main() {
   try {
    var i=new MyInventory(); var r=Run(i,Item("A"),Item("Missing")); Check("whole-bundle invalid before writes",r.Status==LootDeliveryStatus.Retry && i.Adds==0 && !String.IsNullOrWhiteSpace(r.Reason));
    i=new MyInventory(); r=Run(i,Item("A",2),Item("A",3)); Check("duplicates aggregate into one exact insert",r.Status==LootDeliveryStatus.Delivered && i.Adds==1 && i.GetItemAmount(Id("A")).RawValue==5000000);
    i=new MyInventory{MaxVolume=MyInventory.F(1)}; r=Run(i,Item("A"),Item("B")); Check("shared volume exhaustion before writes",r.Status==LootDeliveryStatus.Retry && i.Adds==0 && !String.IsNullOrWhiteSpace(r.Reason));
    i=new MyInventory{MaxMass=MyInventory.F(1)}; r=Run(i,Item("A"),Item("B")); Check("shared mass exhaustion before writes",r.Status==LootDeliveryStatus.Retry && i.Adds==0);
    i=new MyInventory{MaxItemCount=1}; r=Run(i,Item("A"),Item("B")); Check("shared slot exhaustion before writes",r.Status==LootDeliveryStatus.Retry && i.Adds==0);
    i=new MyInventory(); i.Seed(Id("A"),3000000); r=Run(i,Item("A",2),Item("B")); Check("exact insert preserves prior amounts",r.Status==LootDeliveryStatus.Delivered && i.GetItemAmount(Id("A")).RawValue==5000000 && i.GetItemAmount(Id("B")).RawValue==1000000);
    foreach(var mode in new[]{"silent","partial","throw"}) { i=new MyInventory{Mode=mode}; i.Seed(Id("A"),3000000); r=Run(i,Item("A",2)); Check(mode+" verified rollback preserves preexisting",r.Status==LootDeliveryStatus.Retry && i.GetItemAmount(Id("A")).RawValue==3000000); }
    i=new MyInventory{Mode="rollbackfail"}; i.Seed(Id("A"),3000000); r=Run(i,Item("A",2)); Check("rollback failure actionable marker",r.Status==LootDeliveryStatus.ReconciliationRequired && r.Reconciliation!=null && r.Reconciliation.ContainerId==42 && !String.IsNullOrEmpty(r.Reconciliation.AttemptId) && !String.IsNullOrEmpty(r.Reconciliation.Reason) && r.Reconciliation.Items.Count==1 && r.Reconciliation.Items[0].BeforeRaw==3000000 && r.Reconciliation.Items[0].ObservedKnown && r.Reconciliation.Items[0].ObservedRaw==4000000);
    i=new MyInventory{Mode="partial",Unknown=true}; r=Run(i,Item("A",2)); Check("unknown observation requires reconciliation",r.Status==LootDeliveryStatus.ReconciliationRequired && r.Reconciliation!=null && r.Reconciliation.Items.Count==1 && !r.Reconciliation.Items[0].ObservedKnown);
    // Negative control models the old independent preflight: both fit individually, but not together.
    i=new MyInventory{MaxVolume=MyInventory.F(1)}; bool old=i.CanItemsBeAdded(MyInventory.F(1),Id("A")) && i.CanItemsBeAdded(MyInventory.F(1),Id("B")); r=Run(i,Item("A"),Item("B")); Check("negative control detects independent item capacity",old && r.Status==LootDeliveryStatus.Retry && i.Adds==0);
    MyDefinitionManager.Static.ComponentBlocks.Add(Id("Mapped"));
    Sandbox.Engine.Utils.MyFakes.ENABLE_COMPONENT_BLOCKS=false;
    i=new MyInventory(); r=Run(i,Item("Mapped",2)); Check("mapped component flag off exact delivery",r.Status==LootDeliveryStatus.Delivered && i.Adds==1 && i.GetItemAmount(Id("Mapped")).RawValue==2000000);
    Sandbox.Engine.Utils.MyFakes.ENABLE_COMPONENT_BLOCKS=true;
    i=new MyInventory(); r=Run(i,Item("GVKSpecialPackage",2)); Check("unmapped package exact delivery",r.Status==LootDeliveryStatus.Delivered && i.Adds==1 && i.GetItemAmount(Id("GVKSpecialPackage")).RawValue==2000000);
    i=new MyInventory(); r=Run(i,Item("Mapped",2)); Check("mapped component flag on unsupported conversion retries before writes",r.Status==LootDeliveryStatus.Retry && i.Adds==0);
    Sandbox.Engine.Utils.MyFakes.ENABLE_COMPONENT_BLOCKS=false; MyDefinitionManager.Static.ComponentBlocks.Clear();
    ManagerProbe.Tests.Run();
   } catch(Exception e) { Console.WriteLine("ERROR "+e); fail++; }
   Console.WriteLine("TOTAL PASS="+pass+" FAIL="+fail); return fail==0 && pass>0?0:1;
  }
 }
}
'@
$manager=Slice $sources[$names[1]] '        private void ReportLootFailure(' '        // --- Slots'
$captureMethods=Slice $sources[$names[1]] '        private void RunCapture(' '        private static bool Finite('
$failureFields=Slice $sources[$names[1]] '        private sealed class LootFailure' '        private readonly HashSet<long> _checkedNpcContainers'
$saveMethod=Slice $sources[$names[1]] '        public void SaveState()' '        // --- Capture'
$status=Slice $sources[$names[3]] '        public void Status()' '        [Command("start"'
$retention=Slice $sources[$names[4]] '        internal static bool CanForgetSavedZone(' '        private void Check('
$retentionConstant=Slice $sources[$names[4]] '        private static readonly TimeSpan ForgetAfter =' '        private '
$managerHarness=@'
namespace ManagerProbe {
 using GVK.Koth.Core; using GVK.Koth.Zones; using GVK.Koth.Core.Config;
 using System.Text; using System.Globalization; using VRageMath;
 public enum ZoneMode { Other, Encampment }
 public class NpcZoneSave { public DateTime LastSeenUtc; }
 public class CaptureResult { public bool Completed; public long LootDrops; public int PointsEach; public List<long> ScoringFactions=new List<long>(); }
 public class Capture { public double Fraction; public int LootCounter; public long Headroom; public CaptureResult Tick(double dt,IReadOnlyCollection<long> p,long max) { Headroom=max; return new CaptureResult(); } }
 public static class ParticipantScanner { public static IReadOnlyCollection<long> Encampment(ZoneProfile p,Vector3D c)=>new long[0]; public static IReadOnlyCollection<long> Capture(ZoneProfile p,Vector3D c)=>new long[0]; }
 public class Faction { public string Tag="T",Name="N"; public long FactionId; public void RequestChangeBalance(long n) {} }
 public class Factions { public Faction TryGetFactionById(long id)=>null; }
 public class Session { public Factions Factions=new Factions(); }
 public static class MyAPIGateway { public static Session Session=new Session(); }
 public class ZoneRuntime {
  public ZoneSave Save=new ZoneSave(); public ZoneProfile Profile=new ZoneProfile(); public double SecondsToNextLootAttempt;
  public long LootContainerId; public Vector3D Center; public string[] LastFactionTags; public string Name="zone"; public bool Active=true; public int LastParticipantCount; public Capture Capture=new Capture(); public int Stores;
  public void StoreState() { Stores++; }
 }
 public interface IMyCargoContainer { object GetPosition(); }
 public class Container : IMyCargoContainer { public object GetPosition()=>null; }
 public static class LootDelivery {
  public static Queue<LootDeliveryResult> Results=new Queue<LootDeliveryResult>(); public static int Calls;
  public static LootDeliveryResult TryDeliver(ZoneProfile p,Vector3D center,ref long id,out IMyCargoContainer c) { Calls++; c=new Container(); id=42; return Results.Dequeue(); }
 }
 public static class GpsMarkers { public static int Calls; public static object LootColor; public static string LootName(string s)=>s; public static void AddForAll(string a,string b,object c,object d,TimeSpan e,double f) { Calls++; } }
 public class Alerts { public int Calls; public List<string> Messages=new List<string>(); public void Send(string s) { Calls++; Messages.Add(s); } }
 public class Plugin { public Alerts Alerts=new Alerts(); public Config Config=new Config(); public ZoneManager Zones; public Data Data; public Sync Sync; public List<string> ConfigProblems=new List<string>(); }
 public class Sync { public void Completed(ZoneRuntime z,List<string> t) {} }
 public class Config { public bool Enabled=true; }
 public class Announce { public int Calls; public void Chat(string s) { Calls++; } public void Both(string s) { Calls++; } }
 public class State { public List<ZoneSave> Zones=new List<ZoneSave>(); }
 public class Data { public int Saves; public State State=new State(); public void SaveState() { Saves++; } public void AddPoints(long id,string tag,string name,int points,string mode,string profile) {} }
 public class ZoneManager {
  const double LootRetrySeconds=10, LootGpsRange=15000; static readonly TimeSpan LootGpsLifetime=TimeSpan.FromMinutes(15);
  public Plugin _plugin=new Plugin(); public Announce _announce=new Announce(); public Data _data=new Data(); public double _secondsSinceSave;
  public List<ZoneRuntime> Zones=new List<ZoneRuntime>(); public string ForceState(string name)=>"";
  public DateTime Clock=new DateTime(2026,1,1,0,0,0,DateTimeKind.Utc);
  public void Run(ZoneRuntime z) { DeliverQueuedLoot(z.Save,z.Profile,z.Center,Clock); Clock=Clock.AddSeconds(10); }
  public void CaptureRun(ZoneRuntime z) { RunCapture(z,1); }
  public void RewardRun(ZoneRuntime z,CaptureResult r) { Reward(z,r,0); }
'@
$managerHarness += $failureFields + $saveMethod + $captureMethods + $manager + @'
 }
 public class Context { public string Text; public void Respond(string s) { Text=s; } }
 public class Admin { public Plugin Plugin; public Context Context=new Context();
'@
$managerHarness += $status
$managerHarness += @'
 }
 public static class NpcRetention {
'@
$managerHarness += $retentionConstant + $retention
$managerHarness += @'
 }
 public static class Tests {
  static void Check(string n,bool ok) { Probe.Program.Check(n,ok); }
  static LootDeliveryResult Result(LootDeliveryStatus s,LootReconciliationSave m=null)=>new LootDeliveryResult(s,m,"inventory full");
  static void Reset() { LootDelivery.Results.Clear(); LootDelivery.Calls=0; GpsMarkers.Calls=0; }
  public static void Run() {
   Reset(); var m=new ZoneManager(); var z=new ZoneRuntime(); z.Save.PendingLootDrops=2; m.Zones.Add(z);
   LootDelivery.Results.Enqueue(Result(LootDeliveryStatus.Delivered)); LootDelivery.Results.Enqueue(Result(LootDeliveryStatus.Retry)); m.Run(z);
   Check("manager delivered decrements exactly once and emits GPS",z.Save.PendingLootDrops==1 && LootDelivery.Calls==2 && GpsMarkers.Calls==1 && m._data.Saves==1 && z.Stores==1);
   Reset(); m=new ZoneManager(); z=new ZoneRuntime(); z.Save.PendingLootDrops=2; m.Zones.Add(z); LootDelivery.Results.Enqueue(Result(LootDeliveryStatus.Retry)); m.Run(z);
   Check("manager retry retains queue without GPS or save",z.Save.PendingLootDrops==2 && GpsMarkers.Calls==0 && m._data.Saves==0);
   Reset(); m=new ZoneManager(); z=new ZoneRuntime(); z.Save.PendingLootDrops=2; var other=new ZoneRuntime(); other.Save.Name="other"; other.Save.PendingLootDrops=1; m.Zones.Add(z); m.Zones.Add(other);
   var marker=new LootReconciliationSave{AttemptId="attempt",ContainerId=42,Reason="inspect"}; LootDelivery.Results.Enqueue(Result(LootDeliveryStatus.ReconciliationRequired,marker)); m.Run(z);
   Check("manager reconciliation saves marker and all runtimes",z.Save.PendingLootDrops==2 && Object.ReferenceEquals(z.Save.LootReconciliation,marker) && m._data.Saves==1 && z.Stores==1 && other.Stores==1 && m._plugin.Alerts.Calls==1 && GpsMarkers.Calls==0);
   // Model a restored ZoneSave: persistence itself is outside these doubles.
   var restored=new ZoneRuntime{Save=new ZoneSave{PendingLootDrops=z.Save.PendingLootDrops,LootReconciliation=z.Save.LootReconciliation}}; int before=LootDelivery.Calls; m.Run(restored);
   Check("restored marker blocks automatic retry",LootDelivery.Calls==before && restored.Save.PendingLootDrops==2);
   LootDelivery.Results.Enqueue(Result(LootDeliveryStatus.Delivered)); m.Run(other);
   Check("other zone proceeds while restored zone suspended",other.Save.PendingLootDrops==0 && LootDelivery.Calls==before+1 && GpsMarkers.Calls==1);
   Reset(); m=new ZoneManager(); z=new ZoneRuntime(); z.Save.PendingLootDrops=1; m.Zones.Add(z); LootDelivery.Results.Enqueue(Result(LootDeliveryStatus.ReconciliationRequired)); m.Run(z);
   Check("missing outcome marker still suspends",z.Save.LootReconciliation!=null && z.Save.PendingLootDrops==1);
   Reset(); m=new ZoneManager(); z=new ZoneRuntime(); z.Save.Name="retry-key"; z.Save.PendingLootDrops=2; m.Zones.Add(z);
   LootDelivery.Results.Enqueue(Result(LootDeliveryStatus.Retry)); m.Run(z);
   Check("retry alert contains key debt reason and timing",m._plugin.Alerts.Calls==1 && m._plugin.Alerts.Messages[0].Contains("retry-key") && m._plugin.Alerts.Messages[0].Contains("2 owed") && m._plugin.Alerts.Messages[0].Contains("inventory full") && m._plugin.Alerts.Messages[0].Contains("retries 1") && m._plugin.Alerts.Messages[0].Contains("first failure"));
   LootDelivery.Results.Enqueue(Result(LootDeliveryStatus.Retry)); m.Run(z);
   Check("persistent retry alerts once",m._plugin.Alerts.Calls==1 && z.Save.PendingLootDrops==2);
   LootDelivery.Results.Enqueue(Result(LootDeliveryStatus.Delivered)); LootDelivery.Results.Enqueue(Result(LootDeliveryStatus.Retry)); m.Run(z);
   Check("partial drain does not report recovery",z.Save.PendingLootDrops==1 && m._plugin.Alerts.Calls==1);
   LootDelivery.Results.Enqueue(Result(LootDeliveryStatus.Delivered)); m.Run(z);
   Check("full drain reports retry recovery",z.Save.PendingLootDrops==0 && m._plugin.Alerts.Calls==2 && m._plugin.Alerts.Messages[1].Contains("recovered") && m._plugin.Alerts.Messages[1].Contains("retries 3"));
   z.Save.PendingLootDrops=1; LootDelivery.Results.Enqueue(Result(LootDeliveryStatus.Retry)); m.Run(z);
   Check("new failure episode alerts again",m._plugin.Alerts.Calls==3 && z.Save.PendingLootDrops==1);
   Reset(); m=new ZoneManager(); z=new ZoneRuntime(); z.Save.Name="throttled"; z.Save.PendingLootDrops=1; m.Zones.Add(z);
   LootDelivery.Results.Enqueue(Result(LootDeliveryStatus.Retry)); m.Run(z); int calls=LootDelivery.Calls; m.Clock=m.Clock.AddSeconds(-5); m.Run(z);
   Check("retry cadence suppresses early attempt",LootDelivery.Calls==calls && m._plugin.Alerts.Calls==1 && z.Save.PendingLootDrops==1);
   other=new ZoneRuntime(); other.Save.Name="independent"; other.Save.PendingLootDrops=1; m.Zones.Add(other); LootDelivery.Results.Enqueue(Result(LootDeliveryStatus.Retry)); m.Run(other);
   Check("independent zone emits own retry alert",m._plugin.Alerts.Calls==2 && m._plugin.Alerts.Messages[1].Contains("independent"));
   Reset(); m=new ZoneManager(); z=new ZoneRuntime(); z.Save.Name="wide"; z.Save.PendingLootDrops=long.MaxValue; m.Zones.Add(z); m.CaptureRun(z);
   Check("capture receives zero queue headroom at maximum",z.Capture.Headroom==0 && z.Save.PendingLootDrops==long.MaxValue);
   LootDelivery.Results.Enqueue(Result(LootDeliveryStatus.Delivered)); LootDelivery.Results.Enqueue(Result(LootDeliveryStatus.Retry)); m.Run(z); m.CaptureRun(z);
   Check("delivery restores one unit of queue headroom",z.Save.PendingLootDrops==long.MaxValue-1 && z.Capture.Headroom==1);
   bool overflow=false; try { m.RewardRun(z,new CaptureResult{LootDrops=2}); } catch(OverflowException) { overflow=true; }
   Check("checked queue addition rejects overflow before effects",overflow && z.Save.PendingLootDrops==long.MaxValue-1 && z.Stores==1);
   m=new ZoneManager(); z=new ZoneRuntime(); z.Save.PendingLootDrops=1; m.Zones.Add(z);
   var admin=new Admin{Plugin=new Plugin{Zones=m}}; z.Save.LootReconciliation=new LootReconciliationSave{AttemptId="abc",ContainerId=42,Reason="inspect"};
   z.Save.LootReconciliation.Items.Add(new LootReconciliationItemSave{TypeId="Component",SubtypeId="A",RequestedRaw=1250001,BeforeRaw=2000000,ObservedKnown=false});
   admin.Status(); var t=admin.Context.Text; Check("admin status reports exact quantities and unknown",t.Contains("SUSPENDED") && t.Contains("abc") && t.Contains("42") && t.Contains("inspect") && t.Contains("requested 1.250001") && t.Contains("before 2") && t.Contains("observed unknown") && t.Contains("queued drops 1"));
   z.Save.LootReconciliation.Items.Clear(); admin.Status(); Check("admin status empty items",admin.Context.Text.Contains("items unavailable"));
   z.Save.LootReconciliation.Items.Add(null); admin.Status(); Check("admin status null item",admin.Context.Text.Contains("item unavailable"));
   z.Save.LootReconciliation=null; admin.Status(); Check("admin status null marker",!admin.Context.Text.Contains("SUSPENDED"));
   var saved=new ZoneSave{Name="inactive#42",PendingLootDrops=3,LootReconciliation=new LootReconciliationSave{AttemptId="saved-attempt",Reason="inspect"}};
   admin.Plugin.Data=new Data(); admin.Plugin.Data.State.Zones.Add(saved); admin.Status();
   Check("admin status shows inactive saved marker once",admin.Context.Text.Contains("saved zone inactive#42") && admin.Context.Text.Contains("queued drops 3") && admin.Context.Text.Split(new[]{"saved-attempt"},StringSplitOptions.None).Length==2);
   z.Save.Name="active#1"; z.Save.LootReconciliation=new LootReconciliationSave{AttemptId="live-attempt"};
   admin.Plugin.Data.State.Zones.Add(new ZoneSave{Name="ACTIVE#1",LootReconciliation=z.Save.LootReconciliation}); admin.Status();
   Check("admin status avoids duplicate live saved marker",admin.Context.Text.Split(new[]{"live-attempt"},StringSplitOptions.None).Length==2 && admin.Context.Text.Split(new[]{"saved-attempt"},StringSplitOptions.None).Length==2);
   admin.Plugin.Zones=null; admin.Status(); Check("admin status saved marker without live manager",admin.Context.Text.Contains("saved zone inactive#42") && admin.Context.Text.Split(new[]{"saved-attempt"},StringSplitOptions.None).Length==2);
   var now=new DateTime(2026,10,31,0,0,0,DateTimeKind.Utc); var npc=new NpcZoneSave{LastSeenUtc=now.AddDays(-8)}; var unmarked=new ZoneSave();
   Check("npc retention active",!NpcRetention.CanForgetSavedZone(npc,unmarked,now,true));
   npc.LastSeenUtc=now.AddDays(-1); Check("npc retention recent",!NpcRetention.CanForgetSavedZone(npc,unmarked,now,false));
   npc.LastSeenUtc=now.AddDays(-8); Check("npc retention old unmarked",NpcRetention.CanForgetSavedZone(npc,unmarked,now,false));
   unmarked.LootReconciliation=new LootReconciliationSave{AttemptId="retained"}; Check("npc retention old marked",!NpcRetention.CanForgetSavedZone(npc,unmarked,now,false));
   unmarked.LootReconciliation=new LootReconciliationSave(); Check("npc retention empty marker",!NpcRetention.CanForgetSavedZone(npc,unmarked,now,false));
   unmarked.LootReconciliation=null; unmarked.PendingLootDrops=long.MaxValue; Check("npc retention old ordinary debt",!NpcRetention.CanForgetSavedZone(npc,unmarked,now,false));
  }
 }
}
'@
$program=$prefix+"`n"+$state+$middle+$runner+$managerHarness
foreach($name in $names) { if((Get-FileHash -LiteralPath $paths[$name] -Algorithm SHA256).Hash.ToLowerInvariant() -ne $hashes[$name]) { throw "Source changed during extraction: $name" } }
$out=[IO.Path]::GetFullPath($OutputDirectory); [IO.Directory]::CreateDirectory($out) | Out-Null
$utf8=New-Object Text.UTF8Encoding($false); $src=Join-Path $out 'Probe.cs'; $exe=Join-Path $out 'Probe.exe'
[IO.File]::WriteAllText($src,$program,$utf8)
$manifest=[ordered]@{ source_hashes=$hashes; input_hashes_file_hash=(Get-FileHash -LiteralPath $pinsPath -Algorithm SHA256).Hash.ToLowerInvariant(); compiler_hash=(Get-FileHash -LiteralPath $compiler -Algorithm SHA256).Hash.ToLowerInvariant(); references=@($refs | ForEach-Object { [ordered]@{path=$_;sha256=(Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant()} }); generated_source_hash=(Get-FileHash -LiteralPath $src -Algorithm SHA256).Hash.ToLowerInvariant(); limitations=@('Controlled inventory doubles; no actual game API, server, persistence, concurrency, or crash atomicity proof.','Manager and admin methods run against doubles; restored state is simulated, not serialized or loaded by the real persistence stack.','Mapped component conversion with the feature enabled is unsupported and rejected before mutation; no converted-game behavior is tested.','NPC retention helper and admin status use source-extracted methods with saved-state and runtime doubles; tracker refresh and real persistence are not exercised.','Capture Tick is a double: headroom forwarding and checked manager queue addition are exercised, not the real Core capture arithmetic or dormant traversal.') }
[IO.File]::WriteAllText((Join-Path $out 'manifest.json'),($manifest | ConvertTo-Json -Depth 8),$utf8)
$args=@('/nologo','/target:exe','/langversion:latest',"/out:$exe") + @($refs | ForEach-Object { "/reference:$_" }) + @($src)
& $compiler @args *> (Join-Path $out 'compiler.log')
if($LASTEXITCODE -ne 0 -or !(Test-Path -LiteralPath $exe)) { throw 'Compilation failed; inspect compiler.log.' }
$needle='(concrete.IsConstrained && volume > concrete.MaxVolume.RawValue)'
if(([regex]::Matches($program,[regex]::Escape($needle))).Count -ne 1) { throw 'Mutation anchor missing or ambiguous.' }
$mutated=$program.Replace($needle,'false')
$mutSrc=Join-Path $out 'Mutant.cs'; $mutExe=Join-Path $out 'Mutant.exe'
[IO.File]::WriteAllText($mutSrc,$mutated,$utf8)
$mutArgs=@('/nologo','/target:exe','/langversion:latest',"/out:$mutExe") + @($refs | ForEach-Object { "/reference:$_" }) + @($mutSrc)
& $compiler @mutArgs *> (Join-Path $out 'mutant-compiler.log')
if($LASTEXITCODE -ne 0 -or !(Test-Path -LiteralPath $mutExe)) { throw 'Mutation compilation failed; inspect mutant-compiler.log.' }
& $mutExe *> (Join-Path $out 'mutant-cases.log'); $mutCode=$LASTEXITCODE
$mutLines=@(Get-Content -LiteralPath (Join-Path $out 'mutant-cases.log'))
$mutationDetected=($mutCode -ne 0 -and @($mutLines | Where-Object { $_ -eq 'FAIL shared volume exhaustion before writes' }).Count -eq 1)
$manifest['mutation']=[ordered]@{description='Remove whole-bundle volume check in extracted production source';source_hash=(Get-FileHash -LiteralPath $mutSrc -Algorithm SHA256).Hash.ToLowerInvariant();exit_code=$mutCode;detected=$mutationDetected}
if(!$mutationDetected) { [IO.File]::WriteAllText((Join-Path $out 'manifest.json'),($manifest | ConvertTo-Json -Depth 8),$utf8); throw 'Negative mutation was not detected by the volume case.' }
$guard='Sandbox.Engine.Utils.MyFakes.ENABLE_COMPONENT_BLOCKS && MyDefinitionManager.Static.TryGetComponentBlockDefinition(id) != null'
if(([regex]::Matches($program,[regex]::Escape($guard))).Count -ne 1) { throw 'Component guard mutation anchor missing or ambiguous.' }
$guardSource=$program.Replace($guard,'MyDefinitionManager.Static.TryGetComponentBlockDefinition(id) != null')
$guardSrc=Join-Path $out 'GuardMutant.cs'; $guardExe=Join-Path $out 'GuardMutant.exe'
[IO.File]::WriteAllText($guardSrc,$guardSource,$utf8)
$guardArgs=@('/nologo','/target:exe','/langversion:latest',"/out:$guardExe") + @($refs | ForEach-Object { "/reference:$_" }) + @($guardSrc)
& $compiler @guardArgs *> (Join-Path $out 'guard-mutant-compiler.log')
if($LASTEXITCODE -ne 0 -or !(Test-Path -LiteralPath $guardExe)) { throw 'Guard mutation compilation failed; inspect guard-mutant-compiler.log.' }
& $guardExe *> (Join-Path $out 'guard-mutant-cases.log'); $guardCode=$LASTEXITCODE
$guardLines=@(Get-Content -LiteralPath (Join-Path $out 'guard-mutant-cases.log'))
$guardDetected=($guardCode -ne 0 -and @($guardLines | Where-Object { $_ -eq 'FAIL mapped component flag off exact delivery' }).Count -eq 1)
$manifest['guard_mutation']=[ordered]@{description='Restore unconditional mapped component rejection in extracted production source';source_hash=(Get-FileHash -LiteralPath $guardSrc -Algorithm SHA256).Hash.ToLowerInvariant();exit_code=$guardCode;detected=$guardDetected}
if(!$guardDetected) { [IO.File]::WriteAllText((Join-Path $out 'manifest.json'),($manifest | ConvertTo-Json -Depth 8),$utf8); throw 'Unconditional component guard mutation was not detected.' }
& $exe *> (Join-Path $out 'cases.log'); $code=$LASTEXITCODE
$lines=@(Get-Content -LiteralPath (Join-Path $out 'cases.log'))
$caseResults=@($lines | Where-Object { $_ -match '^(PASS|FAIL) ' } | ForEach-Object { [ordered]@{ status=$_.Substring(0,4).Trim(); name=$_.Substring(5) } })
$manifest['cases']=$caseResults; $manifest['exit_code']=$code
[IO.File]::WriteAllText((Join-Path $out 'manifest.json'),($manifest | ConvertTo-Json -Depth 8),$utf8)
if($code -ne 0 -or @($lines | Where-Object { $_ -match '^TOTAL PASS=\d+ FAIL=0$' }).Count -ne 1) { throw 'Behavioral checks failed; inspect cases.log.' }
Write-Output "PASS: controlled source harness; artifacts: $out"
