<#
.SYNOPSIS
Isolated, mocked regression harness for the r005 manager and runtime changes.
.DESCRIPTION
Run only after authorizing execution and building the matching production Core assembly.
The supplied Core assembly must be built from the pinned source; the harness hashes it
but cannot establish that correspondence. The hash manifest is a JSON object mapping the four relative input paths below to SHA256
hex strings. Compiler, references, full runtime and extracted methods are hashed into
provenance.txt. Supply an existing, empty output directory outside both source roots.
NegativeControl also requires the preserved r005 ZoneManager.cs; its SHA256 is pinned
below. It runs the same behavioral assertions with that manager, expecting only the
named F1/F2 assertions to fail. This uses mocked MES, tracker, data and game types; it
is not a game, MES, network, persistence-queue or VM acceptance test.
.EXAMPLE
.\Invoke-NpcRegressionProbe.ps1 -SourceRoot C:\work\GVK-KOTH-Plugin -OutputDirectory D:\probe\run1 -CompilerPath D:\compiler\csc.exe -CoreAssembly D:\built\GVK_KOTH.Core.dll -ReferencePaths @('C:\reference\System.Xml.dll') -InputHashesJson D:\pins.json
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string] $SourceRoot,
    [Parameter(Mandatory=$true)][string] $OutputDirectory,
    [Parameter(Mandatory=$true)][string] $CompilerPath,
    [Parameter(Mandatory=$true)][string] $CoreAssembly,
    [Parameter(Mandatory=$true)][string[]] $ReferencePaths,
    [Parameter(Mandatory=$true)][string] $InputHashesJson,
    [switch] $NegativeControl,
    [string] $PreservedManagerPath
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $SourceRoot).ProviderPath
$out = (Resolve-Path -LiteralPath $OutputDirectory).ProviderPath
$compiler = (Resolve-Path -LiteralPath $CompilerPath).ProviderPath
$core = (Resolve-Path -LiteralPath $CoreAssembly).ProviderPath
$hashFile = (Resolve-Path -LiteralPath $InputHashesJson).ProviderPath
if (-not (Test-Path -LiteralPath $out -PathType Container)) { throw 'OutputDirectory must be an existing directory.' }
if (@(Get-ChildItem -LiteralPath $out -Force).Count -ne 0) { throw 'OutputDirectory must be empty.' }
function Is-Within([string] $child, [string] $parent) {
    $p = [IO.Path]::GetFullPath($parent).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    $c = [IO.Path]::GetFullPath($child).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    return $c.Equals($p, [StringComparison]::OrdinalIgnoreCase) -or $c.StartsWith($p + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)
}
# Reject reparse points in the existing output path, including ancestors: lexical
# containment alone does not prevent junctions redirecting writes into a checkout.
$cursor = Get-Item -LiteralPath $out -Force
while ($null -ne $cursor) {
    if (($cursor.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Output path contains a reparse point: $($cursor.FullName)" }
    $cursor = $cursor.Parent
}
if ((Is-Within $out $root) -or (Is-Within $root $out)) { throw 'Output directory must be separate from the source checkout.' }
$refs = @($ReferencePaths | ForEach-Object { (Resolve-Path -LiteralPath $_).ProviderPath })
if ($NegativeControl) {
    if (-not $PreservedManagerPath) { throw 'NegativeControl requires PreservedManagerPath.' }
    $oldManager = (Resolve-Path -LiteralPath $PreservedManagerPath).ProviderPath
    if ((Get-FileHash -LiteralPath $oldManager -Algorithm SHA256).Hash -ne 'ceb8068facae4ef63df4ee512c71f9d3066dc56742f9a56af5dc4d117e9429ca') { throw 'Preserved r005 manager hash mismatch.' }
    $oldRoot = Split-Path (Split-Path (Split-Path $oldManager -Parent) -Parent) -Parent
    if ((Is-Within $out $oldRoot) -or (Is-Within $oldRoot $out)) { throw 'Output directory must be separate from preserved source.' }
}
$pins = Get-Content -LiteralPath $hashFile -Raw | ConvertFrom-Json
$inputs = @('GVK_KOTH/Zones/ZoneManager.cs','GVK_KOTH/Zones/ZoneRuntime.cs','GVK_KOTH.Core/KothState.cs','GVK_KOTH.Core/ZoneCapture.cs')
$source = @{}; $inputPaths = @{}; $provenance = [Collections.Generic.List[string]]::new()
foreach ($relative in $inputs) {
    $property = $pins.PSObject.Properties[$relative]
    if ($null -eq $property -or $property.Value -notmatch '^[0-9a-fA-F]{64}$') { throw "Missing SHA256 pin: $relative" }
    $path = (Resolve-Path -LiteralPath (Join-Path $root $relative)).ProviderPath
    $actual = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    if ($actual -ne $property.Value) { throw "Input hash mismatch: $relative" }
    $inputPaths[$relative] = $path
    $source[$relative] = [IO.File]::ReadAllText($path)
    $provenance.Add("input $relative $actual")
}
foreach ($path in @($hashFile, $compiler, $core) + $refs) {
    $provenance.Add("file $path $((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash)")
}
if ($NegativeControl) {
    $managerText = [IO.File]::ReadAllText($oldManager)
    $provenance.Add("negative-manager $oldManager $((Get-FileHash -LiteralPath $oldManager -Algorithm SHA256).Hash)")
} else { $managerText = $source['GVK_KOTH/Zones/ZoneManager.cs'] }

function Hash-Text([string] $text) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($text))).Replace('-', '') }
    finally { $sha.Dispose() }
}
# Lexically skip C# comments, strings and characters while balancing braces.
# Unrecognized source shapes fail instead of substituting a modeled algorithm.
function Get-MethodBlock([string] $text, [string] $name) {
    $pattern = '(?m)^[ \t]*(?:public|private|internal|protected)\s+(?:static\s+)?[^\r\n;{}=]+\b' + [regex]::Escape($name) + '\s*\([^;{}]*\)\s*\{'
    $matches = [regex]::Matches($text, $pattern)
    if ($matches.Count -ne 1) { throw "Extraction drift: expected one block-bodied $name, found $($matches.Count)" }
    $start=$matches[0].Index; $open=$start+$matches[0].Length-1; $depth=0; $state='code'; $escape=$false
    for ($i=$open; $i -lt $text.Length; $i++) {
        $c=$text[$i]; $next=if ($i+1 -lt $text.Length) { $text[$i+1] } else { [char]0 }
        if ($state -eq 'line') { if ($c -eq "`n") { $state='code' }; continue }
        if ($state -eq 'comment') { if ($c -eq '*' -and $next -eq '/') { $state='code'; $i++ }; continue }
        if ($state -eq 'string' -or $state -eq 'char') {
            if ($escape) { $escape=$false; continue }
            if ($c -eq '\') { $escape=$true; continue }
            if (($state -eq 'string' -and $c -eq '"') -or ($state -eq 'char' -and $c -eq "'")) { $state='code' }; continue
        }
        if ($state -eq 'verbatim') { if ($c -eq '"') { if ($next -eq '"') { $i++ } else { $state='code' } }; continue }
        if ($c -eq '/' -and $next -eq '/') { $state='line'; $i++; continue }
        if ($c -eq '/' -and $next -eq '*') { $state='comment'; $i++; continue }
        if ($c -eq '@' -and $next -eq '"') { $state='verbatim'; $i++; continue }
        if ($c -eq '"') { $state='string'; continue }
        if ($c -eq "'") { $state='char'; continue }
        if ($c -eq '{') { $depth++ }
        if ($c -eq '}') { $depth--; if ($depth -eq 0) { return $text.Substring($start,$i-$start+1) } }
    }
    throw "Extraction drift: unterminated $name"
}
function Get-Expression([string] $text, [string] $pattern, [string] $label) {
    $matches=[regex]::Matches($text,$pattern,[Text.RegularExpressions.RegexOptions]::Multiline)
    if ($matches.Count -ne 1) { throw "Extraction drift: expected one $label, found $($matches.Count)" }
    return $matches[0].Value
}
$methods = [Collections.Generic.List[string]]::new()
foreach ($name in @('GetOrPick','UpdateSlots','SpawnIfNeeded','SaveState','Stop','SendWarnings','ProfilePosition')) {
    $piece=Get-MethodBlock $managerText $name
    $methods.Add($piece); $provenance.Add("extracted $name $(Hash-Text $piece)")
}
foreach ($entry in @(
    @('Profile','^[ \t]*public\s+ZoneProfile\s+Profile\s*\([^;{}]*\)\s*=>[^;]+;'),
    @('Find','^[ \t]*public\s+ZoneRuntime\s+Find\s*\([^;{}]*\)\s*=>[^;]+;'),
    @('Zones','^[ \t]*public\s+IEnumerable<ZoneRuntime>\s+Zones\s*=>[^;]+;'),
    @('Config','^[ \t]*private\s+KothConfig\s+Config\s*=>[^;]+;')
)) {
    $piece=Get-Expression $managerText $entry[1] $entry[0]
    $methods.Add($piece); $provenance.Add("extracted $($entry[0]) $(Hash-Text $piece)")
}
$runtime=$source['GVK_KOTH/Zones/ZoneRuntime.cs']
if ([regex]::Matches($runtime,'\bpublic\s+ZoneRuntime\s*\(').Count -ne 2 -or [regex]::Matches($runtime,'\bpublic\s+void\s+StoreState\s*\(').Count -ne 1) { throw 'Extraction drift: runtime constructors or StoreState changed shape.' }
$provenance.Add("extracted full-ZoneRuntime $(Hash-Text $runtime)")
$prefix = @'
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Xml.Serialization;
using GVK.Koth.Core;
using GVK.Koth.Core.Config;
using GVK.Koth.Services;
using VRageMath;
namespace VRageMath { public struct Vector3D { public Vector3D(double x,double y,double z) {} } }
namespace Sandbox.Game.Entities {
    public class MyCubeGrid { public bool Closed, MarkedForClose; public PositionComponent PositionComp = new PositionComponent(); }
    public class PositionComponent { public Bounds WorldAABB = new Bounds(); }
    public class Bounds { public VRageMath.Vector3D Center; }
}
namespace Sandbox.ModAPI { public interface IMyRemoteControl { bool Closed {get;} bool IsFunctional {get;} } }
namespace GVK.Koth {
    public class Logger { public void Info(string s) {} public void Warn(string s) {} }
    public class MesMock { public int Calls; public bool Result=true; public bool Spawn(string s, Vector3D p) { Calls++; return Result; } }
    public class AlertsMock { public readonly List<string> Messages=new List<string>(); public void Send(string s) { Messages.Add(s); } }
    public class Plugin { public static Logger Log=new Logger(); public KothConfig Config=new KothConfig(); public MesMock Mes=new MesMock(); public AlertsMock Alerts=new AlertsMock(); }
}
namespace GVK.Koth.Services {
    public class Announcer { public void Chat(string s) {} public void Discord(string s) {} }
    public static class GpsMarkers {
        public static object UpcomingColor = new object();
        public static string ZoneName(string n) { return n; }
        public static void AddForAll(string n,string d,Vector3D p,object c,TimeSpan t) {}
    }
    public class KothData {
        public KothState State=new KothState(); public string Serialized; public readonly List<string> Events=new List<string>();
        public void SaveState() { Events.Add("serialize"); var x=new XmlSerializer(typeof(KothState)); using(var w=new StringWriter()) { x.Serialize(w,State); Serialized=w.ToString(); } }
    }
}
namespace GVK.Koth.Zones {
    public class NpcZoneTracker {
        public readonly List<ZoneRuntime> Zones=new List<ZoneRuntime>(); private readonly KothData data;
        public NpcZoneTracker(KothData d) { data=d; }
        public void Stop() { data.Events.Add("tracker-stop"); foreach(var z in Zones) z.StoreState(); Zones.Clear(); }
    }
    public class CycleRandom : Random { int n; public override int Next(int maxValue) { return (n++) % maxValue; } }
    public sealed class ZoneManager {
        private readonly GVK.Koth.Plugin _plugin;
        private readonly KothData _data;
        private readonly Announcer _announce;
        private readonly Random _random=new CycleRandom();
        private readonly Dictionary<string,ZoneRuntime> _zones=new Dictionary<string,ZoneRuntime>(StringComparer.OrdinalIgnoreCase);
        private readonly NpcZoneTracker _npc;
        private readonly Dictionary<string,DateTime> _runningSlots=new Dictionary<string,DateTime>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _warningsSent=new HashSet<string>();
        private readonly HashSet<string> _spawnFailuresReported=new HashSet<string>();
        private double _secondsSinceSave;
        public ZoneManager(GVK.Koth.Plugin p,KothData d) { _plugin=p; _data=d; _announce=new Announcer(); _npc=new NpcZoneTracker(d); }
        public void AddFixed(ZoneRuntime z) { _zones.Add(z.Name,z); }
        public void AddNpc(ZoneRuntime z) { _npc.Zones.Add(z); }
        public int NpcCount { get { return _npc.Zones.Count; } }
        public HashSet<string> RunSlots(DateTime now) { return UpdateSlots(now); }
        public string Pick(SlotConfig s,DateTime start) { return GetOrPick(s,start); }
'@
$suffix = @'
    }
}
'@
$runner = @'
namespace Probe {
using GVK.Koth;
using GVK.Koth.Zones;
using Sandbox.Game.Entities;
class Program {
    static int failures=0; static bool negative;
    static void Check(string id,bool ok,bool negativeExpected=false) {
        bool expectedFailure=negative && negativeExpected;
        bool pass=expectedFailure ? !ok : ok;
        Console.WriteLine((pass ? "PASS " : "FAIL ")+id+(expectedFailure ? " (expected preserved-source failure)" : ""));
        if(!pass) failures++;
    }
    static ZoneProfile Profile(string name,bool enabled=true,bool npc=false,double share=0.25) {
        return new ZoneProfile { Name=name, Enabled=enabled, Attach=npc ? ZoneAttach.NpcTag : ZoneAttach.Coordinates,
            Activation=ZoneActivation.Slot, SpawnGroup=npc ? "airship" : "", CaptureSeconds=100,
            MinPlayers=1, MinPresenceShare=share, PointsPerCompletion=5, PointsPerLoot=10 };
    }
    static SlotConfig Slot(params string[] names) { return new SlotConfig { Name="slot", Enabled=true, DurationMinutes=30, Pool=new List<string>(names) }; }
    static DateTime ActiveTime(SlotConfig s) { return WeeklySchedule.LastStart(s.Start,DateTime.UtcNow).AddMinutes(1); }
    static KothState ReadState(string xml) { using(var r=new StringReader(xml)) return (KothState)new XmlSerializer(typeof(KothState)).Deserialize(r); }
    static string WriteZone(ZoneSave z) { using(var w=new StringWriter()) { new XmlSerializer(typeof(ZoneSave)).Serialize(w,z); return w.ToString(); } }
    static ZoneSave ReadZone(string xml) { using(var r=new StringReader(xml)) return (ZoneSave)new XmlSerializer(typeof(ZoneSave)).Deserialize(r); }
    static ZoneRuntime Fixed(ZoneProfile p,ZoneSave s) { return new ZoneRuntime(p,s); }
    static ZoneRuntime Npc(ZoneProfile p,ZoneSave s) { return new ZoneRuntime(p,s,new NpcZoneSave { GridId=1, Profile=p.Name },new MyCubeGrid(),null); }
    static void Slots() {
        var p=new Plugin(); var d=new KothData(); var m=new ZoneManager(p,d);
        var n=Profile("npc",true,true); p.Config.Zones.Add(n); var s=Slot("npc"); p.Config.Slots.Add(s);
        var now=ActiveTime(s); var chosen=m.RunSlots(now);
        Check("F1 NPC-only chosen before grid and spawn attempted",m.NpcCount==0 && chosen.Contains("npc") && p.Mes.Calls==1 && d.State.SlotPicks.Single().Spawned,true);
        m.RunSlots(now); Check("spawn once per occurrence",p.Mes.Calls<=1);
        var q=new Plugin(); var e=new KothData(); var z=new ZoneManager(q,e); var f=Profile("fixed"); var a=Profile("npc",true,true);
        q.Config.Zones.Add(f); q.Config.Zones.Add(a); q.Config.Zones.Add(Profile("disabled",false)); z.AddFixed(Fixed(f,new ZoneSave()));
        var pool=Slot("fixed","npc","disabled","missing"); var t=ActiveTime(pool);
        string first=z.Pick(pool,t); string second=z.Pick(pool,t.AddDays(7));
        Check("F1 mixed pool retains fixed and NPC",new[]{first,second}.Contains("fixed") && new[]{first,second}.Contains("npc"),true);
        Check("disabled and missing excluded",first!="disabled" && first!="missing" && second!="disabled" && second!="missing");
        var bad=Slot("disabled","missing"); bad.Name="bad"; Check("disabled and missing only returns null",z.Pick(bad,t)==null);
        var fixedOnly=Slot("fixed"); fixedOnly.Name="fixed-only"; Check("fixed-only control",z.Pick(fixedOnly,t)=="fixed");
        var existing=Slot("disabled"); existing.Name="saved"; e.State.SlotPicks.Add(new SlotPick { Slot=existing.Name,StartUtc=t.AddDays(21),Zone="saved",Spawned=true });
        Check("saved occurrence pick reused",z.Pick(existing,t.AddDays(21))=="saved");
        var sp=new Plugin(); var sd=new KothData(); var sm=new ZoneManager(sp,sd); var sn=Profile("saved-npc",true,true);
        sp.Config.Zones.Add(sn); var ss=Slot("saved-npc"); ss.Name="saved-slot"; sp.Config.Slots.Add(ss);
        var st=ActiveTime(ss); sd.State.SlotPicks.Add(new SlotPick {Slot=ss.Name,StartUtc=WeeklySchedule.LastStart(ss.Start,st),Zone=sn.Name,Spawned=true});
        Check("saved spawned pick remains active without respawn",sm.RunSlots(st).Contains(sn.Name) && sp.Mes.Calls==0);
    }
    static void Persistence() {
        var p=new Plugin(); var d=new KothData(); var m=new ZoneManager(p,d);
        var fs=new ZoneSave {Name="fixed",PendingLootDrops=2}; var ns=new ZoneSave {Name="npc#1",PendingLootDrops=3};
        d.State.Zones.Add(fs); d.State.Zones.Add(ns);
        var f=Fixed(Profile("fixed"),fs); var n=Npc(Profile("npc",true,true),ns); m.AddFixed(f); m.AddNpc(n);
        f.Capture.Progress=45; f.Capture.LootCounter=4; n.Capture.Progress=75; n.Capture.LootCounter=5;
        m.SaveState(); var saved=ReadState(d.Serialized);
        var sf=saved.GetZone("fixed"); var sn=saved.GetZone("npc#1");
        Check("periodic fixed scalar and pending loot",sf.Progress==45 && sf.LootCounter==4 && sf.PendingLootDrops==2);
        Check("F2 periodic NPC scalar and pending loot",sn.Progress==75 && sn.LootCounter==5 && sn.PendingLootDrops==3,true);
        f.Capture.Progress=50; f.Capture.LootCounter=6; n.Capture.Progress=80; n.Capture.LootCounter=7;
        d.Events.Clear(); m.Stop(); saved=ReadState(d.Serialized); sf=saved.GetZone("fixed"); sn=saved.GetZone("npc#1");
        Check("Stop serializes before tracker cleanup",d.Events.SequenceEqual(new[]{"serialize","tracker-stop"}) && m.NpcCount==0);
        Check("Stop fixed scalar and pending loot",sf.Progress==50 && sf.LootCounter==6 && sf.PendingLootDrops==2);
        Check("F2 Stop NPC serialized before cleanup",sn.Progress==80 && sn.LootCounter==7 && sn.PendingLootDrops==3,true);
    }
    static void Runtime() {
        var p=Profile("runtime"); var save=new ZoneSave { Name="runtime", FactionProgress=new List<FactionProgressSave>(), FactionProgressSpecified=true, LootCounter=7, PendingLootDrops=3 };
        var rt=Fixed(p,save); rt.Capture.Tick(90,new long[]{1}); rt.StoreState();
        Check("StoreState saves detached snapshot and flag",save.FactionProgressSpecified && save.FactionProgress.Count==1 && save.FactionProgress[0].Progress==90);
        var copied=ReadZone(WriteZone(save)); var restored=Npc(p,copied);
        save.FactionProgress[0].Progress=1; copied.FactionProgress[0].Progress=2;
        Check("constructor and save detach credit",rt.Capture.SnapshotFactionProgress()[1]==90 && restored.Capture.SnapshotFactionProgress()[1]==90);
        var result=restored.Capture.Tick(10,new long[]{1});
        var uninterrupted=Fixed(p,new ZoneSave {FactionProgress=new List<FactionProgressSave>(),FactionProgressSpecified=true,LootCounter=7});
        uninterrupted.Capture.Tick(90,new long[]{1}); var expected=uninterrupted.Capture.Tick(10,new long[]{1});
        Check("runtime restart matches uninterrupted reward and loot",result.Completed && result.ScoringFactions.SequenceEqual(expected.ScoringFactions) && result.ScoringFactions.SequenceEqual(new long[]{1}) && result.PointsEach==expected.PointsEach && result.LootDrops==expected.LootDrops && restored.Capture.LootCounter==uninterrupted.Capture.LootCounter && copied.PendingLootDrops==3);
        var late=Fixed(p,new ZoneSave {FactionProgress=new List<FactionProgressSave>(),FactionProgressSpecified=true});
        late.Capture.Tick(90,new long[]{1}); late.StoreState(); var lateRestart=Fixed(p,ReadZone(WriteZone(late.Save)));
        var lateResult=lateRestart.Capture.Tick(10,new long[]{2});
        Check("genuine late arrival does not score",lateResult.Completed && lateResult.ScoringFactions.Count==0);
        var legacy=ReadZone("<ZoneSave><Name>legacy</Name><Progress>90</Progress><LootCounter>7</LootCounter><PendingLootDrops>3</PendingLootDrops></ZoneSave>");
        Check("legacy XML absence marker",!legacy.FactionProgressSpecified && !WriteZone(legacy).Contains("<FactionProgress"));
        var legacyRuntime=Fixed(p,legacy); Check("legacy positive share resets bar only",legacyRuntime.Capture.Progress==0 && legacyRuntime.Capture.LootCounter==7 && legacy.PendingLootDrops==3 && legacyRuntime.Capture.SnapshotFactionProgress().Count==0);
        var initialized=new ZoneSave {Progress=90,LootCounter=7,PendingLootDrops=3,FactionProgress=new List<FactionProgressSave>(),FactionProgressSpecified=false};
        Check("initialized but unspecified is legacy",Fixed(p,initialized).Capture.Progress==0);
        var nil=new ZoneSave {Progress=90,LootCounter=7,PendingLootDrops=3,FactionProgress=null,FactionProgressSpecified=true};
        Check("specified null is unknown",Fixed(p,nil).Capture.Progress==0);
        var zero=ReadZone("<ZoneSave><Progress>90</Progress><LootCounter>7</LootCounter><PendingLootDrops>3</PendingLootDrops></ZoneSave>");
        var zr=Fixed(Profile("zero",true,false,0),zero); Check("legacy share zero retains scalar",zr.Capture.Progress==90 && zr.Capture.LootCounter==7 && zero.PendingLootDrops==3);
        var empty=ReadZone("<ZoneSave><Progress>90</Progress><LootCounter>7</LootCounter><PendingLootDrops>3</PendingLootDrops><FactionProgress /></ZoneSave>");
        var er=Fixed(p,empty); Check("known empty XML retains bar",empty.FactionProgressSpecified && empty.FactionProgress!=null && empty.FactionProgress.Count==0 && er.Capture.Progress==90);
        Check("known empty gives no invented eligibility",er.Capture.Tick(10,new long[]{1}).ScoringFactions.Count==0);
        er.StoreState(); Check("empty snapshot is explicitly written",ReadZone(WriteZone(empty)).FactionProgressSpecified);
    }
    static int Main(string[] args) {
        negative=args.Length==1 && args[0]=="negative";
        Console.WriteLine("Mocked manager and game dependencies; no actual MES, game, queue or VM.");
        try { Slots(); Persistence(); Runtime(); } catch(Exception e) { Console.WriteLine("ERROR "+e); return 2; }
        Console.WriteLine("Assertion failures: "+failures); return failures==0 ? 0 : 1;
    }
}
}
'@
# Runtime contains top-level using directives and must precede declarations in a
# separate compilation unit. Keep exact bytes as read, without rewriting its code.
$program=$prefix + "`n" + ($methods -join "`n") + "`n" + $suffix + "`n" + $runner
$provenance.Add("generated program $(Hash-Text $program)")
# Revalidate inputs after extraction, before writing or invoking anything.
foreach ($relative in $inputs) {
    if ((Get-FileHash -LiteralPath $inputPaths[$relative] -Algorithm SHA256).Hash -ne $pins.PSObject.Properties[$relative].Value) { throw "Input changed during extraction: $relative" }
}
if ($NegativeControl -and (Get-FileHash -LiteralPath $oldManager -Algorithm SHA256).Hash -ne 'ceb8068facae4ef63df4ee512c71f9d3066dc56742f9a56af5dc4d117e9429ca') { throw 'Preserved manager changed during extraction.' }
$utf8=New-Object System.Text.UTF8Encoding($false)
$programPath=Join-Path $out 'Probe.cs'; $runtimePath=Join-Path $out 'ZoneRuntime.cs'; $exe=Join-Path $out 'Probe.exe'
[IO.File]::WriteAllText($programPath,$program,$utf8)
[IO.File]::WriteAllText($runtimePath,$runtime,$utf8)
[IO.File]::WriteAllLines((Join-Path $out 'provenance.txt'),$provenance,$utf8)
# Copy the production Core assembly for CLR resolution, without altering the input.
Copy-Item -LiteralPath $core -Destination (Join-Path $out ([IO.Path]::GetFileName($core)))
$arguments=@('/nologo','/target:exe','/langversion:latest',"/out:$exe", "/reference:$core")
foreach ($reference in $refs) { $arguments += "/reference:$reference" }
$arguments += @($programPath,$runtimePath)
# Keep compiler and serializer temporary files in the isolated directory as well.
$previousTemp=$env:TEMP; $previousTmp=$env:TMP
try {
    $env:TEMP=$out; $env:TMP=$out
    Push-Location -LiteralPath $out
    try {
        & $compiler @arguments *> (Join-Path $out 'compile.log')
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $exe)) { throw "Compilation failed; see $(Join-Path $out 'compile.log')" }
        $runArgs=@(); if ($NegativeControl) { $runArgs=@('negative') }
        & $exe @runArgs *> (Join-Path $out 'result.log')
        if ($LASTEXITCODE -ne 0) { throw "Probe failed (exit $LASTEXITCODE); see $(Join-Path $out 'result.log')" }
    } finally { Pop-Location }
} finally { $env:TEMP=$previousTemp; $env:TMP=$previousTmp }
Write-Output "Mocked probe completed; inspect artifacts in $out"
