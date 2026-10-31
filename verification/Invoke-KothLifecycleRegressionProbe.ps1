<#
.SYNOPSIS
Source-extracted controlled lifecycle regression harness for r007.
.DESCRIPTION
Execution requires parent authorization, settled source pins, and a matching
Core assembly. InputHashesJson maps each relative source path (including
docs/GVK_KOTH.sample.cfg) and @core to SHA256.
The source-to-assembly relationship must be established separately.
The manifest pins each current source and the independent frozen sources. Outputs
are controlled doubles, not live game, MES, inventory, webhook, or restart proof.
The two linked-block implementations and NpcTag are compiled as exact complete
source files. Game entities and session ownership are controlled doubles; no live
game or ownership-change event is exercised. The frozen prior class is an
independent negative control, not a mutant made from the new class.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string] $SourceRoot,
    [Parameter(Mandatory=$true)][string] $OldLinkedBlocks,
    [Parameter(Mandatory=$true)][string] $CompilerPath,
    [Parameter(Mandatory=$true)][string] $CoreAssembly,
    [Parameter(Mandatory=$true)][string] $OldSourceRoot,
    [Parameter(Mandatory=$true)][string[]] $ReferencePaths,
    [Parameter(Mandatory=$true)][string] $InputHashesJson,
    [Parameter(Mandatory=$true)][string] $OutputDirectory
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $SourceRoot).ProviderPath
$old = (Resolve-Path -LiteralPath $OldLinkedBlocks).ProviderPath
$compiler = (Resolve-Path -LiteralPath $CompilerPath).ProviderPath
$pinsPath = (Resolve-Path -LiteralPath $InputHashesJson).ProviderPath
$out = (Resolve-Path -LiteralPath $OutputDirectory).ProviderPath
if (@(Get-ChildItem -LiteralPath $out -Force).Count -ne 0) { throw 'Output directory must be empty.' }
function Inside([string] $child, [string] $parent) {
    $p = [IO.Path]::GetFullPath($parent).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    $c = [IO.Path]::GetFullPath($child).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    return $c.Equals($p,[StringComparison]::OrdinalIgnoreCase) -or $c.StartsWith($p + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)
}
$cursor = Get-Item -LiteralPath $out -Force
while ($null -ne $cursor) {
    if (($cursor.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Output path has a reparse point.' }
    $cursor = $cursor.Parent
}
$oldRoot = Split-Path (Split-Path (Split-Path $old -Parent) -Parent) -Parent
if ((Inside $out $root) -or (Inside $root $out) -or (Inside $out $oldRoot) -or (Inside $oldRoot $out)) { throw 'Output directory must be separate from both source trees.' }
$pins = Get-Content -LiteralPath $pinsPath -Raw | ConvertFrom-Json
$files = @('GVK_KOTH/Zones/LinkedBlocks.cs','GVK_KOTH.Core/NpcTag.cs')
$paths = @{}
$provenance = [Collections.Generic.List[string]]::new()
foreach ($file in $files) {
    $pin = $pins.PSObject.Properties[$file]
    if ($null -eq $pin -or $pin.Value -notmatch '^[0-9a-fA-F]{64}$') { throw "Missing SHA256 pin: $file" }
    $path = (Resolve-Path -LiteralPath (Join-Path $root $file)).ProviderPath
    $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    if ($hash -ne $pin.Value) { throw "Input hash mismatch: $file" }
    $paths[$file] = $path
    $provenance.Add("source $file $path $hash")
}
$oldHash = (Get-FileHash -LiteralPath $old -Algorithm SHA256).Hash
if ($oldHash -ne '0925a9b3c3d207d7dffc0ceefdf66baebaaf8730b1db58ccd363827e59732093') { throw 'Frozen negative-control source hash mismatch.' }
foreach ($path in @($old,$compiler,$pinsPath,$PSCommandPath)) { $provenance.Add("file $path $((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash)") }
$mock = @'
using System;
using System.Collections.Generic;
using System.Linq;
using GVK.Koth.Zones;
using Sandbox.Game.Entities;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using VRageMath;
namespace VRageMath {
 public struct Vector3D {
  public double X,Y,Z;
  public Vector3D(double x,double y,double z) { X=x; Y=y; Z=z; }
  public static double DistanceSquared(Vector3D a,Vector3D b) { return (a.X-b.X)*(a.X-b.X)+(a.Y-b.Y)*(a.Y-b.Y)+(a.Z-b.Z)*(a.Z-b.Z); }
 }
}
namespace Sandbox.ModAPI {
 public interface IMyFunctionalBlock {
  long OwnerId {get;} string CustomData {get;} bool Closed {get;} bool Enabled {get;set;}
  MyCubeGrid CubeGrid {get;} Vector3D GetPosition();
 }
}
namespace Sandbox.Game.Entities {
 public class MyEntity { public bool Closed; }
 public class MyCubeGrid : MyEntity {
  public long EntityId; public List<long> BigOwners=new List<long>(); public List<object> Blocks=new List<object>();
  public IEnumerable<object> GetFatBlocks() { return Blocks; }
 }
 public static class MyEntities {
  public static Dictionary<long,MyEntity> Entities=new Dictionary<long,MyEntity>();
  public static bool TryGetEntityById(long id,out MyEntity e) { return Entities.TryGetValue(id,out e); }
 }
}
namespace Sandbox.Game.World {
 public class Players { public bool IdentityIsNpc(long id) { return id==1; } }
 public class MySession { public static MySession Static=new MySession(); public Players Players=new Players(); }
}
namespace GVK.Koth.Zones {
 public class Profile { public double LinkRange; }
 public class ZoneRuntime { public string Name; public Profile Profile=new Profile(); public Vector3D Center; }
}
namespace Probe {
 class Block : IMyFunctionalBlock {
  public long owner=1; public string tag="[GVK-KOTH-Link]"; public bool closed,on; public MyCubeGrid grid; public Vector3D position;
  public long OwnerId {get{return owner;}} public string CustomData {get{return tag;}} public bool Closed {get{return closed;}}
  public bool Enabled {get{return on;} set{on=value;}} public MyCubeGrid CubeGrid {get{return grid;}} public Vector3D GetPosition(){return position;}
 }
 class Program {
  static int failures;
  static void Check(string name,bool value) { Console.WriteLine((value?"PASS ":"FAIL ")+name); if(!value) failures++; }
  static MyCubeGrid Grid(long id,long owner,Block b) { var g=new MyCubeGrid { EntityId=id }; g.BigOwners.Add(owner); b.grid=g; g.Blocks.Add(b); MyEntities.Entities[id]=g; return g; }
  static int Main() {
   var zone=new ZoneRuntime { Name="alpha", Center=new Vector3D(0,0,0), Profile=new Profile { LinkRange=10 } };
   var open=new List<ZoneRuntime>{zone}; var none=new List<ZoneRuntime>();
   var b=new Block(); var g=Grid(1,1,b); var links=new LinkedBlocks(); links.Scan(g); links.Apply(open);
   Check("NPC ranged link enables",b.on); links.Apply(none); Check("NPC link closes",!b.on);
   links.Apply(open); b.owner=2; links.Apply(none); Check("transferred block remains enabled",b.on && !links.IsKnown(1));
   var b2=new Block(); var g2=Grid(2,1,b2); links.Scan(g2); links.Apply(open); g2.BigOwners[0]=2; links.Apply(none);
   Check("transferred grid remains enabled",b2.on && !links.IsKnown(2));
   var b3=new Block(); var g3=Grid(3,2,b3); links.Scan(g3); links.Apply(open); Check("player grid excluded at scan",!b3.on && !links.IsKnown(3));
   var b4=new Block {tag="[GVK-KOTH-Link:alpha]",position=new Vector3D(100,0,0)}; var g4=Grid(4,1,b4); links.Scan(g4); links.Apply(open);
   Check("named link ignores range",b4.on); g4.Closed=true; links.Apply(none); Check("closed grid removed",!links.IsKnown(4));
   var b5=new Block {position=new Vector3D(100,0,0)}; var g5=Grid(5,1,b5); links.Scan(g5); links.Apply(open); Check("ranged link outside radius stays off",!b5.on);
   return failures==0 ? 0 : 1;
  }
 }
}
'@
$mockPath = Join-Path $out 'ownership-doubles.cs'
[IO.File]::WriteAllText($mockPath,$mock,[Text.UTF8Encoding]::new($false))
$provenance.Add("generated $mockPath $((Get-FileHash -LiteralPath $mockPath -Algorithm SHA256).Hash)")
$results = @{}
foreach ($entry in @(@('current',$paths['GVK_KOTH/Zones/LinkedBlocks.cs']),@('old',$old))) {
    $label=$entry[0]; $exe=Join-Path $out "$label.exe"; $log=Join-Path $out "$label.log"
    $arguments=@('/nologo','/target:exe','/langversion:latest',"/out:$exe",$mockPath,$paths['GVK_KOTH.Core/NpcTag.cs'],$entry[1])
    $provenance.Add("compiler-arguments $label $($arguments | ConvertTo-Json -Compress)")
    & $compiler @arguments *> (Join-Path $out "$label.compile.log")
    if ($LASTEXITCODE -ne 0) { throw "Compiler failed for $label; inspect compile log." }
    $provenance.Add("binary $label $((Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash)")
    & $exe *> $log
    $results[$label]=$LASTEXITCODE
    $provenance.Add("execution $label exit=$LASTEXITCODE logsha256=$((Get-FileHash -LiteralPath $log -Algorithm SHA256).Hash)")
}
[IO.File]::WriteAllLines((Join-Path $out 'provenance.txt'),$provenance,[Text.UTF8Encoding]::new($false))
if ($results.current -ne 0 -or $results.old -ne 1) { throw 'Ownership regression or negative control did not produce the expected exit codes.' }
$oldLog = Get-Content -LiteralPath (Join-Path $out 'old.log') -Raw
foreach ($id in @('transferred block remains enabled','transferred grid remains enabled','player grid excluded at scan')) {
    if (-not $oldLog.Contains("FAIL $id")) { throw "Old-source control did not detect: $id" }
}

# The lifecycle executable extracts production blocks verbatim. Changes to a block
# boundary fail closed; neither the manager nor locator algorithms are reimplemented.
function Slice([string] $text, [string] $start, [string] $end) {
    $a=$text.IndexOf($start,[StringComparison]::Ordinal)
    if ($a -lt 0 -or $text.IndexOf($start,$a+$start.Length,[StringComparison]::Ordinal) -ge 0) { throw "Missing or ambiguous extraction start: $start" }
    $b=$text.IndexOf($end,$a+$start.Length,[StringComparison]::Ordinal)
    if ($b -lt 0) { throw "Missing extraction end: $end" }
    return $text.Substring($a,$b-$a)
}
$core=(Resolve-Path -LiteralPath $CoreAssembly).ProviderPath
$oldRoot=(Resolve-Path -LiteralPath $OldSourceRoot).ProviderPath
$references=@($ReferencePaths | ForEach-Object { (Resolve-Path -LiteralPath $_).ProviderPath })
$corePin=$pins.PSObject.Properties['@core']
if ($null -eq $corePin -or $corePin.Value -notmatch '^[0-9a-fA-F]{64}$' -or (Get-FileHash -LiteralPath $core -Algorithm SHA256).Hash -ne $corePin.Value) { throw 'Missing or mismatched @core SHA256 pin.' }
if ((Inside $out $oldRoot) -or (Inside $oldRoot $out)) { throw 'Output overlaps the frozen source tree.' }
$extra=@('GVK_KOTH/Zones/ZoneManager.cs','GVK_KOTH/Zones/NpcZoneTracker.cs','GVK_KOTH/Zones/LootDelivery.cs','GVK_KOTH/Plugin.cs','GVK_KOTH/Commands/KothAdminCommands.cs','docs/GVK_KOTH.sample.cfg')
$texts=@{}
foreach($file in $extra) {
    $pin=$pins.PSObject.Properties[$file]
    if($null -eq $pin -or $pin.Value -notmatch '^[0-9a-fA-F]{64}$') { throw "Missing SHA256 pin: $file" }
    $path=(Resolve-Path -LiteralPath (Join-Path $root $file)).ProviderPath
    $hash=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    if($hash -ne $pin.Value) { throw "Input hash mismatch: $file" }
    $texts[$file]=[IO.File]::ReadAllText($path)
    $provenance.Add("source $file $path $hash")
}
foreach($path in @($core)+$references) { $provenance.Add("reference $path $((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash)") }
$oldTracker=(Resolve-Path -LiteralPath (Join-Path $oldRoot 'GVK_KOTH/Zones/NpcZoneTracker.cs')).ProviderPath
if ((Get-FileHash -LiteralPath $oldTracker -Algorithm SHA256).Hash -ne '0fe9ebc21e2147d5a3991609043ac8c6a73ec196673a39652dfe3e21bf6cc027') { throw 'Frozen tracker hash mismatch.' }
$provenance.Add("old-tracker $oldTracker $((Get-FileHash -LiteralPath $oldTracker -Algorithm SHA256).Hash)")
$samplePath=(Resolve-Path -LiteralPath (Join-Path $root 'docs/GVK_KOTH.sample.cfg')).ProviderPath
$provenance.Add("resource GVK_KOTH.sample.cfg $samplePath $((Get-FileHash -LiteralPath $samplePath -Algorithm SHA256).Hash)")
$manager=$texts['GVK_KOTH/Zones/ZoneManager.cs']; $tracker=$texts['GVK_KOTH/Zones/NpcZoneTracker.cs']; $loot=$texts['GVK_KOTH/Zones/LootDelivery.cs']; $plugin=$texts['GVK_KOTH/Plugin.cs']; $admin=$texts['GVK_KOTH/Commands/KothAdminCommands.cs']
$sections=[ordered]@{
 rebuild=(Slice $manager '        public void Rebuild()' '        public void Tick(double dt)')
 start=(Slice $plugin '        private void StartSession()' '        private void StopSession()')
 stopSession=(Slice $plugin '        private void StopSession()' '        public override void Update()')
 stopManager=(Slice $manager '        public void Stop()' '        /// <summary>Builds the zones')
 tick=(Slice $manager '        public void Tick(double dt)' '        /// <summary>True while any zone')
 publish=(Slice $manager '        private void PublishActiveProfiles(' '        private bool FixedActive(')
 delivery=(Slice $manager '        private static bool Finite(' '        // --- Slots')
 refresh=(Slice $tracker '        public void Refresh(DateTime now)' '        private void Check(')
 oldRetention=(Slice ([IO.File]::ReadAllText($oldTracker)) '        internal static bool CanForgetSavedZone(' '        private void Check(')
 locator=(Slice $loot '        private static IMyCargoContainer FindContainer(' ("    }`n}"))
 check=(Slice $tracker '        private void Check(long gridId, DateTime now)' '        private static bool IsNpc(')
 npcOwner=(Slice $tracker '        private static bool IsNpc(long identityId)' ("    }`n}"))
 hasContainer=(Slice $loot '        internal static bool HasContainer(' '        public static LootDeliveryResult TryDeliver(')
 load=(Slice $plugin '        public void LoadConfig()' '        public UserControl GetControl()')
 reload=(Slice $admin '        public void Reload()' '        [Command("status"')
}
foreach($key in $sections.Keys) {
    $bytes=[Text.Encoding]::UTF8.GetBytes($sections[$key]); $sha=[Security.Cryptography.SHA256]::Create()
    try { $hash=[BitConverter]::ToString($sha.ComputeHash($bytes)).Replace('-','') } finally { $sha.Dispose() }
    $provenance.Add("extracted $key $hash")
}
$prefix=@'
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.IO;
using System.Text;
using System.Xml.Serialization;
using System.Reflection;
using GVK.Koth.Core;
using GVK.Koth.Core.Config;
using GVK.Koth.Zones;
using GVK.Koth.Services;
using Sandbox.Game.Entities;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using VRage.Game.Entity;
using VRageMath;
namespace VRageMath {
 public struct Vector3D { public double X,Y,Z; public Vector3D(double x,double y,double z){X=x;Y=y;Z=z;} public static double DistanceSquared(Vector3D a,Vector3D b){return Math.Pow(a.X-b.X,2)+Math.Pow(a.Y-b.Y,2)+Math.Pow(a.Z-b.Z,2);} }
 public struct BoundingSphereD { public Vector3D Center; public double Radius; public BoundingSphereD(Vector3D c,double r){Center=c;Radius=r;} }
}
namespace VRage.Game.Entity { public class MyEntity { public bool Closed; } }
namespace Sandbox.ModAPI {
 public interface IMyRemoteControl { bool Closed {get;} string CustomData {get;} long OwnerId {get;} }
 public interface IMyTerminalBlock { string CustomName {get;} }
 public interface IMyCargoContainer : IMyTerminalBlock { bool Closed {get;} bool IsFunctional {get;} object CubeGrid {get;} long EntityId {get;} Vector3D GetPosition(); }
 public class Utilities { public List<string> Writes=new List<string>(); public void SetVariable(string name,bool value){Writes.Add(name+"="+value);GVK.Koth.Services.Trace.Events.Add("sandbox:"+name+"="+value);} }
 public static class MyAPIGateway { public static object Session=new object(); public static Utilities Utilities=new Utilities(); }
}
namespace Sandbox.Game.Entities {
 public class MyCubeGrid : MyEntity { public long EntityId; public bool MarkedForClose; public string DisplayName="mock"; public List<long> BigOwners=new List<long>(); public List<object> Blocks=new List<object>(); public IEnumerable<object> GetFatBlocks(){return Blocks;} }
 public static class MyEntities { public static Dictionary<long,MyEntity> Map=new Dictionary<long,MyEntity>(); public static bool TryGetEntityById(long id,out MyEntity entity){return Map.TryGetValue(id,out entity);} }
 public static class MyGamePruningStructure { public static List<MyEntity> Entities=new List<MyEntity>(); public static void GetAllTopMostEntitiesInSphere(ref BoundingSphereD sphere,List<MyEntity> result){result.AddRange(Entities);} }
}
namespace Sandbox.Game.World { public class Players { public bool IdentityIsNpc(long id){return id==1;} } public class MySession { public static MySession Static=new MySession(); public Players Players=new Players(); } }
namespace GVK.Koth.Services {
 public class Alerts { public List<string> Messages=new List<string>(); public void Send(string s){Messages.Add(s);} }
 public static class Trace { public static List<string> Events=new List<string>(); }
 public class Api { public List<string> Changes=new List<string>(); public void ZoneChanged(string n,bool v){Changes.Add(n+"="+v);Trace.Events.Add("listener:"+n+"="+v);} }
 public class Sync { public void Tick(ZoneManager m,double d,DateTime n){} } public class ClientSync : Sync {} public class MesBridge {} public class ModApi : Api { public ModApi(GVK.Koth.Plugin p){} public void Register(){} public void Unregister(){Trace.Events.Add("unregister");} }
 public class Announcer { public void Chat(string s){} public void Both(string s){} }
 public static class GpsMarkers { public static object LootColor; public static string LootName(string n){return n;} public static void AddForAll(string a,string b,Vector3D c,object d,TimeSpan e,double f){} }
 public class KothData { public KothData(){} public KothData(string path){} public KothState State=new KothState(); public int Saves; public bool ThrowOnSave; public void SaveState(){Trace.Events.Add("save");if(ThrowOnSave)throw new InvalidOperationException("test save failure");Saves++;} public void Dispose(){Trace.Events.Add("dispose");} }
}
namespace GVK.Koth {
 public class Logger { public void Warn(string s){} public void Info(string s){} public void Error(Exception e,string s){} }
 public class Plugin {
  public static Logger Log=new Logger(); public static Plugin Instance;
  private const string ConfigFileName="GVK_KOTH.cfg"; private KothConfig _config;
  public KothConfig Config {get{return _config;}} public IReadOnlyList<string> ConfigProblems {get;private set;}=new List<string>();
  public bool LastConfigLoadAccepted {get;private set;} public string StoragePath;
  public Alerts Alerts=new Alerts(); public ModApi Api; public ClientSync Sync; public MesBridge Mes; public Announcer Announce=new Announcer(); public KothData Data; public ZoneManager Zones; public string DataPath {get{return StoragePath;}} private System.Diagnostics.Stopwatch _clock=new System.Diagnostics.Stopwatch(); public void StartProbe(){StartSession();} public void StopProbe(){StopSession();}
'@
$prefix += $sections.start + $sections.stopSession + $sections.load + @'
 }
}
namespace GVK.Koth.Zones {
 public enum LootDeliveryStatus { Retry,Delivered,ReconciliationRequired }
 public class LootDeliveryResult { public LootDeliveryStatus Status; public LootReconciliationSave Reconciliation; public string Reason; }
 public static class LootDelivery {
  private static readonly List<MyEntity> Entities=new List<MyEntity>();
  public static Queue<LootDeliveryResult> Results=new Queue<LootDeliveryResult>(); public static int Calls; public static List<string> Attempts=new List<string>();
  public static LootDeliveryResult TryDeliver(ZoneProfile p,Vector3D center,ref long id,out IMyCargoContainer container){Calls++; Attempts.Add(p.Name+"@"+center.X); container=null; return Results.Count>0?Results.Dequeue():new LootDeliveryResult{Status=LootDeliveryStatus.Retry,Reason="missing"};}
'@
$prefix += $sections.hasContainer + $sections.locator + @'
 }
 public class Capture { public int Resets; public void Reset(){Resets++;} }
 public class ZoneRuntime { public ZoneRuntime(){} public ZoneRuntime(ZoneProfile p,ZoneSave s){Profile=p;Save=s;Center=new Vector3D(p.X,p.Y,p.Z);} public ZoneRuntime(ZoneProfile p,ZoneSave s,NpcZoneSave n,MyCubeGrid g,IMyRemoteControl r){Profile=p;Save=s;Npc=n;} public ZoneProfile Profile; public ZoneSave Save; public NpcZoneSave Npc; public bool GridGone,Active,EndedEarly; public string[] LastFactionTags; public Capture Capture=new Capture(); public Vector3D Center; public bool IsNpc {get{return Npc!=null;}} public string Name {get{return Profile.Name;}} public void StoreState(){} }
 public class Links { public int Calls; public void Scan(MyCubeGrid g){} public void Apply(List<ZoneRuntime> z){Calls++;} }
 public class NpcZoneTracker {
  private static readonly TimeSpan ForgetAfter=TimeSpan.FromDays(7);
  private ConcurrentQueue<(long GridId,DateTime DueUtc)> _toCheck=new ConcurrentQueue<(long,DateTime)>(); private List<(long GridId,DateTime DueUtc)> _notYetDue=new List<(long,DateTime)>();
  private Dictionary<long,ZoneRuntime> _zones=new Dictionary<long,ZoneRuntime>(); private KothData _data; private GVK.Koth.Plugin _plugin; private HashSet<long> _unknownProfileReported=new HashSet<long>();
  public Links Links=new Links(); public IEnumerable<ZoneRuntime> Zones {get{return _zones.Values;}} public NpcZoneTracker(GVK.Koth.Plugin p,KothData data){_plugin=p;_data=data;} public void Rebuild(){_zones.Clear();} public void Start(){} public void Stop(){GVK.Koth.Services.Trace.Events.Add("tracker-stop");} public void Reveal(long id,DateTime now){_toCheck.Enqueue((id,now));} public void Add(long id,ZoneRuntime z){_zones[id]=z;}
'@
$prefix += $sections.refresh + $sections.check + $sections.npcOwner + @'
 }
 public static class OldRetention {
  private static readonly TimeSpan ForgetAfter=TimeSpan.FromDays(7);
'@
$prefix += $sections.oldRetention + @'
 }
 public class Rules { public int Calls; public int ActiveCount; public void Apply(List<ZoneRuntime> z,DateTime n){Calls++;ActiveCount=z.Count(x=>x.Active);} }
 public class ZoneManager {
  const double LootRetrySeconds=10,StateSaveSeconds=60,LootGpsRange=15000; static readonly TimeSpan LootGpsLifetime=TimeSpan.FromMinutes(15);
  private sealed class LootFailure { public DateTime FirstUtc; public long RetryCount; }
  private Dictionary<string,LootFailure> _lootFailures=new Dictionary<string,LootFailure>(StringComparer.OrdinalIgnoreCase);
  private Dictionary<string,DateTime> _lootNextAttempt=new Dictionary<string,DateTime>(StringComparer.OrdinalIgnoreCase);
  private HashSet<long> _checkedNpcContainers=new HashSet<long>(); private HashSet<string> _activeProfiles=new HashSet<string>(StringComparer.OrdinalIgnoreCase), _publishedProfiles=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
  private GVK.Koth.Plugin _plugin; private KothData _data; private Announcer _announce=new Announcer(); private NpcZoneTracker _npc; private Rules _rules=new Rules(); private double _secondsSinceSave;
  private Dictionary<string,ZoneRuntime> _zones=new Dictionary<string,ZoneRuntime>(StringComparer.OrdinalIgnoreCase); public IEnumerable<ZoneRuntime> Zones {get{return _zones.Values.Concat(_npc.Zones);}} private KothConfig Config {get{return _plugin.Config;}}
  public int CaptureCalls; public int ActiveRuleZones {get{return _rules.ActiveCount;}} public ZoneManager(GVK.Koth.Plugin p,KothData d){_plugin=p;_data=d;_npc=new NpcZoneTracker(p,d);} public ZoneManager(GVK.Koth.Plugin p,KothData d,Announcer a):this(p,d){_announce=a;}
  public void AddFixed(ZoneRuntime z){_zones[z.Name]=z;} public void AddNpc(long id,ZoneRuntime z){_npc.Add(id,z);} public void AllowRetry(string key){_lootNextAttempt.Remove(key);} public void Publish(List<ZoneRuntime> z){PublishActiveProfiles(z);}
  public ZoneProfile Profile(string name){return Config.Zones.FirstOrDefault(z=>String.Equals(z.Name,name,StringComparison.OrdinalIgnoreCase));}
  public void Start(){} public void SaveState(){_data.SaveState();} private HashSet<string> UpdateSlots(DateTime n){return new HashSet<string>();}
  private bool NpcActive(ZoneRuntime z,HashSet<string> s,DateTime n){return !z.Npc.Ended && !z.GridGone;} private bool FixedActive(ZoneRuntime z,HashSet<string> s){return z.Profile.Enabled;}
  private void RunCapture(ZoneRuntime z,double d){CaptureCalls++;} private void AnnounceStartOrEnd(ZoneRuntime z){} private void CheckWeeklyRollup(DateTime n){}
'@
$prefix += $sections.stopManager + $sections.rebuild + $sections.tick + $sections.publish + $sections.delivery + @'
 }
}
namespace GVK.Koth.Commands {
 public class Context { public string Text; public void Respond(string s){Text=s;} }
 public class KothAdminCommands { private static GVK.Koth.Plugin Plugin {get{return GVK.Koth.Plugin.Instance;}} public Context Context=new Context();
'@
$prefix += $sections.reload + @'
 }
}
'@
$runner=@'
namespace LifecycleProbe {
 using GVK.Koth;
 using GVK.Koth.Commands;
 class Remote : IMyRemoteControl { public bool Closed {get{return false;}} public string CustomData {get{return "GVK-KOTH=air;";}} public long OwnerId {get{return 1;}} }
 class Cargo : IMyCargoContainer { public bool closed,functional=true; public string name="loot"; public MyCubeGrid grid; public long id; public Vector3D position; public bool Closed {get{return closed;}} public bool IsFunctional {get{return functional;}} public string CustomName {get{return name;}} public object CubeGrid {get{return grid;}} public long EntityId {get{return id;}} public Vector3D GetPosition(){return position;} }
 class Program {
  static int failures;
  static void Check(string n,bool value){Console.WriteLine((value?"PASS ":"FAIL ")+n);if(!value)failures++;}
  static string Serialize<T>(T value){using(var w=new StringWriter()){new XmlSerializer(typeof(T)).Serialize(w,value);return w.ToString();}}
  static T Deserialize<T>(string value){using(var r=new StringReader(value))return (T)new XmlSerializer(typeof(T)).Deserialize(r);}
  static void WriteConfig(string dir,KothConfig config){File.WriteAllText(Path.Combine(dir,"GVK_KOTH.cfg"),Serialize(config),System.Text.Encoding.Unicode);}
  static LootDeliveryResult R(LootDeliveryStatus status,string reason=""){return new LootDeliveryResult{Status=status,Reason=reason};}
  static ZoneProfile Profile(string name,ZoneAttach attach){return new ZoneProfile{Name=name,Attach=attach,Activation=ZoneActivation.AlwaysOn,X=10,Radius=10,LootContainerName="loot",PointsPerLoot=1,Loot=new List<LootItem>{new LootItem{TypeId="Component",SubtypeId="SteelPlate",Amount=1}}};}
  static MyCubeGrid Grid(long owner,double x){var g=new MyCubeGrid();g.BigOwners.Add(owner);var c=new Cargo{grid=g,position=new Vector3D(x,0,0)};g.Blocks.Add(c);MyGamePruningStructure.Entities.Add(g);return g;}
  static int Main(string[] args){
   var dir=args[0]; var samplePath=args[1];
   var bootstrapDir=Path.Combine(dir,"bootstrap");Directory.CreateDirectory(bootstrapDir);
   var bootstrap=new Plugin{StoragePath=bootstrapDir};bootstrap.LoadConfig();var bootPath=Path.Combine(bootstrapDir,"GVK_KOTH.cfg");
   bool sampleWritten=File.Exists(bootPath) && File.ReadAllBytes(bootPath).SequenceEqual(File.ReadAllBytes(samplePath));
   Check("absent config creates exact embedded sample and accepts it",sampleWritten && bootstrap.LastConfigLoadAccepted && bootstrap.Config!=null && bootstrap.Config.Zones.Count>0);
   if(!bootstrap.LastConfigLoadAccepted) Console.WriteLine("BOOTSTRAP DIAGNOSTICS: "+String.Join(" | ",bootstrap.ConfigProblems));
   var fixedP=Profile("fixed",ZoneAttach.Coordinates); var npcP=Profile("air",ZoneAttach.NpcTag);
   var config=new KothConfig();config.Zones.Add(fixedP);config.Zones.Add(npcP);WriteConfig(dir,config);
   var p=new Plugin{StoragePath=dir};Plugin.Instance=p;var originalBytes=File.ReadAllBytes(Path.Combine(dir,"GVK_KOTH.cfg"));p.LoadConfig();Check("valid configuration accepted",p.LastConfigLoadAccepted);
   Check("existing valid config bytes unchanged",originalBytes.SequenceEqual(File.ReadAllBytes(Path.Combine(dir,"GVK_KOTH.cfg"))));
   if(!p.LastConfigLoadAccepted || p.Config==null || p.Config.Zones==null || p.Config.Zones.Count<2){Console.WriteLine("CONFIG DIAGNOSTICS: "+String.Join(" | ",p.ConfigProblems));return 1;}
   p.StartProbe();Check("startup actual session method reports fixed absence, NPC unknown",p.Zones!=null && p.ConfigProblems.Count==1 && p.ConfigProblems[0].Contains("fixed"));
   var data=new KothData();var m=new ZoneManager(p,data);p.Zones=m;p.Api=new ModApi(p);var fixedZone=new ZoneRuntime{Profile=p.Config.Zones[0],Save=new ZoneSave{Name="fixed"},Center=new Vector3D(10,0,0)};m.AddFixed(fixedZone);
   MyGamePruningStructure.Entities.Clear();Check("missing fixed container, unspawned NPC unknown",m.CheckLootContainers().Count==1 && m.CheckLootContainers()[0].Contains("fixed"));
   var playerGrid=Grid(2,10);Check("player-owned container rejected",m.CheckLootContainers().Count==1);MyGamePruningStructure.Entities.Clear();
   var farGrid=Grid(1,100);Check("out-of-range container rejected",m.CheckLootContainers().Count==1);MyGamePruningStructure.Entities.Clear();
   var validGrid=Grid(1,10);Check("actual locator accepts in-range NPC grid",m.CheckLootContainers().Count==0);MyGamePruningStructure.Entities.Clear();
   var command=new KothAdminCommands();command.Reload();Check("actual reload response and alert contain missing diagnostic",command.Context.Text.Contains("no eligible") && p.Alerts.Messages.Any(s=>s.Contains("runtime config") && s.Contains("fixed")) && p.LastConfigLoadAccepted);
   var previous=p.Config;var bad=Deserialize<KothConfig>(Serialize(config));bad.Zones[0].Radius=double.NaN;WriteConfig(dir,bad);var invalidBytes=File.ReadAllBytes(Path.Combine(dir,"GVK_KOTH.cfg"));command.Reload();
   Check("invalid existing config bytes unchanged",invalidBytes.SequenceEqual(File.ReadAllBytes(Path.Combine(dir,"GVK_KOTH.cfg"))));
   Check("rejected reload retains previous and reports finite error",Object.ReferenceEquals(previous,p.Config) && !p.LastConfigLoadAccepted && command.Context.Text.Contains("rejected") && command.Context.Text.Contains("finite") && p.Alerts.Messages.Any(s=>s.Contains("config rejected") && s.Contains("finite")));
   WriteConfig(dir,config);p.LoadConfig();foreach(var fixedRuntime in m.Zones.Where(z=>!z.IsNpc)) fixedRuntime.Profile.Enabled=false;npcP=p.Config.Zones.First(z=>z.Name=="air");
   var now=DateTime.UtcNow;var npc=new NpcZoneSave{Profile="air",GridId=17,Ended=true,LastSeenUtc=now.AddDays(-8)};
   var save=new ZoneSave{Name=npc.SaveKey,PendingLootDrops=2,LootPosition=new LootPositionSave{X=42,Y=3,Z=4}};data.State.NpcZones.Add(npc);data.State.Zones.Add(save);
   var emptyNpc=new NpcZoneSave{Profile="air",GridId=18,LastSeenUtc=now.AddDays(-8)};data.State.NpcZones.Add(emptyNpc);data.State.Zones.Add(new ZoneSave{Name=emptyNpc.SaveKey});
   var tracker=new NpcZoneTracker(p,data);tracker.Refresh(now);Check("expired positive debt retained and empty expired cleaned",data.State.NpcZones.Contains(npc) && !data.State.NpcZones.Contains(emptyNpc));
   Check("independent old retention control detects lost ordinary debt",OldRetention.CanForgetSavedZone(npc,save,now,false));
   var revealed=new MyCubeGrid{EntityId=17};revealed.Blocks.Add(new Remote());MyEntities.Map[17]=revealed;tracker.Reveal(17,now);tracker.Refresh(now);Check("ended revealed NPC never recreates runtime",!tracker.Zones.Any() && npc.Ended);
   data.State=Deserialize<KothState>(Serialize(data.State));save=data.State.Zones.Single(z=>z.Name==npc.SaveKey);npc=data.State.NpcZones.Single();
   Check("XML reload keeps same save key and finite location",save.Name==npc.SaveKey && save.PendingLootDrops==2 && save.LootPosition.X==42 && npc.Ended);
   LootDelivery.Calls=0;LootDelivery.Attempts.Clear();LootDelivery.Results.Clear();LootDelivery.Results.Enqueue(R(LootDeliveryStatus.Retry,"missing"));
   int captures=m.CaptureCalls;m.Tick(0);Check("ended dormant queue retries at saved position without capture",save.PendingLootDrops==2 && LootDelivery.Calls==1 && LootDelivery.Attempts[0]=="air@42" && m.CaptureCalls==captures && m.ActiveRuleZones==0);
   var firstAlerts=p.Alerts.Messages.Count; m.Tick(0);Check("retry cadence avoids duplicate attempt",LootDelivery.Calls==1 && p.Alerts.Messages.Count==firstAlerts);
   m.AllowRetry(save.Name);LootDelivery.Results.Enqueue(R(LootDeliveryStatus.Retry,"full"));m.Tick(0);Check("repeated retry keeps full debt and suppresses duplicate alert",save.PendingLootDrops==2 && p.Alerts.Messages.Count==firstAlerts);
   m.AllowRetry(save.Name);LootDelivery.Results.Enqueue(R(LootDeliveryStatus.Delivered));LootDelivery.Results.Enqueue(R(LootDeliveryStatus.Retry,"full"));m.Tick(0);Check("partial drain does not send recovery",save.PendingLootDrops==1 && p.Alerts.Messages.Count==firstAlerts);
   m.AllowRetry(save.Name);LootDelivery.Results.Enqueue(R(LootDeliveryStatus.Delivered));m.Tick(0);var recovery=p.Alerts.Messages.Last();Check("full drain reports age and retries",save.PendingLootDrops==0 && recovery.Contains("recovered") && recovery.Contains("retries 3") && recovery.Contains("first failure"));
   save.PendingLootDrops=1;m.AllowRetry(save.Name);LootDelivery.Results.Enqueue(R(LootDeliveryStatus.Retry,"full"));m.Tick(0);Check("new failure episode alerts again",p.Alerts.Messages.Count==firstAlerts+2 && p.Alerts.Messages.Last().Contains("full") && p.Alerts.Messages.Last().Contains("retries 1"));
   var unknownNpc=new NpcZoneSave{Profile="air",GridId=19,Ended=true,LastSeenUtc=now};var unknown=new ZoneSave{Name=unknownNpc.SaveKey,PendingLootDrops=1};data.State.NpcZones.Add(unknownNpc);data.State.Zones.Add(unknown);
   int priorCalls=LootDelivery.Calls;m.Tick(0);Check("unknown XML position blocks delivery and alerts",unknown.PendingLootDrops==1 && LootDelivery.Calls==priorCalls && p.Alerts.Messages.Any(s=>s.Contains(unknown.Name) && s.Contains("position")));
   var absentNpc=new NpcZoneSave{Profile="missing",GridId=21,Ended=true,LastSeenUtc=now};var absent=new ZoneSave{Name=absentNpc.SaveKey,PendingLootDrops=1,LootPosition=new LootPositionSave{X=3}};data.State.NpcZones.Add(absentNpc);data.State.Zones.Add(absent);m.Tick(0);Check("missing profile blocks independent queue",absent.PendingLootDrops==1 && p.Alerts.Messages.Any(s=>s.Contains(absent.Name) && s.Contains("profile")));
   var suspendedNpc=new NpcZoneSave{Profile="air",GridId=22,Ended=true,LastSeenUtc=now};var suspended=new ZoneSave{Name=suspendedNpc.SaveKey,PendingLootDrops=1,LootPosition=new LootPositionSave{X=3},LootReconciliation=new LootReconciliationSave{Reason="inspect"}};data.State.NpcZones.Add(suspendedNpc);data.State.Zones.Add(suspended);priorCalls=LootDelivery.Calls;m.Tick(0);Check("reconciliation marker suspends dormant delivery",suspended.PendingLootDrops==1 && LootDelivery.Calls==priorCalls);
   var liveNpc=new NpcZoneSave{Profile="air",GridId=20,LastSeenUtc=now};var liveSave=new ZoneSave{Name=liveNpc.SaveKey,PendingLootDrops=1,LootPosition=new LootPositionSave{X=8}};data.State.NpcZones.Add(liveNpc);data.State.Zones.Add(liveSave);var live=new ZoneRuntime{Profile=npcP,Save=liveSave,Npc=liveNpc,Center=new Vector3D(8,0,0)};m.AddNpc(20,live);LootDelivery.Results.Enqueue(R(LootDeliveryStatus.Retry,"missing"));priorCalls=LootDelivery.Calls;m.Tick(0);Check("live and saved traversal attempt once",LootDelivery.Calls==priorCalls+1);
   live.GridGone=true;m.AllowRetry(liveSave.Name);LootDelivery.Results.Enqueue(R(LootDeliveryStatus.Delivered));captures=m.CaptureCalls;m.Tick(0);Check("concealed runtime removed then saved debt recovered",liveSave.PendingLootDrops==0 && m.CaptureCalls==captures);
   var pub=new ZoneManager(p,new KothData());var active=new ZoneRuntime{Profile=npcP,Active=true};pub.Publish(new List<ZoneRuntime>{active});p.Api.Changes.Clear();MyAPIGateway.Utilities.Writes.Clear();
   p.Config.Zones.Remove(npcP);pub.Publish(new List<ZoneRuntime>());pub.Publish(new List<ZoneRuntime>());Check("removed profile publishes false and listener once",MyAPIGateway.Utilities.Writes.Count(x=>x=="GVK_KOTH_air_Active=False")==1 && p.Api.Changes.Count(x=>x=="air=False")==1);
   p.Config.Zones.Add(npcP);pub.Publish(new List<ZoneRuntime>{active});p.Api.Changes.Clear();MyAPIGateway.Utilities.Writes.Clear();npcP.Name="renamed";pub.Publish(new List<ZoneRuntime>());pub.Publish(new List<ZoneRuntime>());Check("rename closes old profile once",MyAPIGateway.Utilities.Writes.Count(x=>x=="GVK_KOTH_air_Active=False")==1 && p.Api.Changes.Count(x=>x=="air=False")==1);
   pub.Publish(new List<ZoneRuntime>{active});p.Api.Changes.Clear();MyAPIGateway.Utilities.Writes.Clear();npcP.Mode=ZoneMode.RulesOnly;pub.Publish(new List<ZoneRuntime>());pub.Publish(new List<ZoneRuntime>());Check("RulesOnly conversion closes once",MyAPIGateway.Utilities.Writes.Count(x=>x=="GVK_KOTH_renamed_Active=False")==1 && p.Api.Changes.Count(x=>x=="renamed=False")==1);
   var stopping=new Plugin{StoragePath=dir};stopping.LoadConfig();stopping.Api=new ModApi(stopping);stopping.Data=new KothData();stopping.Sync=new ClientSync();stopping.Mes=new MesBridge();
   var stoppedManager=new ZoneManager(stopping,stopping.Data);stopping.Zones=stoppedManager;var activeStop=new ZoneRuntime{Profile=stopping.Config.Zones[0],Active=true};stoppedManager.Publish(new List<ZoneRuntime>{activeStop});
   GVK.Koth.Services.Trace.Events.Clear();stopping.StopProbe();var events=GVK.Koth.Services.Trace.Events;
   int sandbox=events.IndexOf("sandbox:GVK_KOTH_"+activeStop.Name+"_Active=False"), close=events.IndexOf("listener:"+activeStop.Name+"=False"), unregister=events.IndexOf("unregister"), trackerStop=events.IndexOf("tracker-stop"), dispose=events.IndexOf("dispose");
   Check("stop closes listener before API unregister and cleans up",sandbox>=0 && close>sandbox && unregister>close && trackerStop>close && unregister>trackerStop && dispose>unregister && stopping.Api==null && stopping.Zones==null && stopping.Sync==null && stopping.Mes==null && stopping.Data==null);
   var failingStop=new Plugin{StoragePath=dir};failingStop.LoadConfig();failingStop.Api=new ModApi(failingStop);failingStop.Data=new KothData{ThrowOnSave=true};failingStop.Zones=new ZoneManager(failingStop,failingStop.Data);
   GVK.Koth.Services.Trace.Events.Clear();failingStop.StopProbe();Check("stop failure still unregisters and disposes",GVK.Koth.Services.Trace.Events.SequenceEqual(new[]{"save","unregister","dispose"}) && failingStop.Api==null && failingStop.Zones==null && failingStop.Data==null);
   return failures==0?0:1;
  }
 }
}
'@
foreach($file in $extra) { if ((Get-FileHash -LiteralPath (Join-Path $root $file) -Algorithm SHA256).Hash -ne $pins.PSObject.Properties[$file].Value) { throw "Source changed during extraction: $file" } }
$src=Join-Path $out 'lifecycle.cs';$exe=Join-Path $out 'lifecycle.exe';$log=Join-Path $out 'lifecycle.log'
[IO.File]::WriteAllText($src,$prefix+$runner,[Text.UTF8Encoding]::new($false))
$provenance.Add("generated $src $((Get-FileHash -LiteralPath $src -Algorithm SHA256).Hash)")
$arguments=@('/nologo','/target:exe','/langversion:latest',"/out:$exe",("/reference:"+$core),("/resource:"+$samplePath+",GVK_KOTH.sample.cfg")) + @($references | ForEach-Object { "/reference:$_" }) + @($src)
$provenance.Add("compiler-arguments lifecycle $($arguments | ConvertTo-Json -Compress)")
& $compiler @arguments *> (Join-Path $out 'lifecycle.compile.log')
if($LASTEXITCODE -ne 0) { [IO.File]::WriteAllLines((Join-Path $out 'provenance.txt'),$provenance,[Text.UTF8Encoding]::new($false)); throw 'Lifecycle compiler failed; inspect compile log.' }
Copy-Item -LiteralPath $core -Destination $out
$provenance.Add("binary lifecycle $((Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash)")
& $exe $out $samplePath *> $log
$code=$LASTEXITCODE
$provenance.Add("execution lifecycle exit=$code logsha256=$((Get-FileHash -LiteralPath $log -Algorithm SHA256).Hash)")
[IO.File]::WriteAllLines((Join-Path $out 'provenance.txt'),$provenance,[Text.UTF8Encoding]::new($false))
if($code -ne 0) { throw 'Lifecycle assertions failed; inspect lifecycle.log.' }
