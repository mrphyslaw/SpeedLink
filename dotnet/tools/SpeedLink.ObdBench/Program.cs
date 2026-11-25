using System.Text.Json;
using SpeedLink.Core;
using SpeedLink.Hardware;
using SpeedLink.J2534;

string Get(string key) { int at=Array.IndexOf(args,key); return at>=0 && at+1<args.Length ? args[at+1] : throw new ArgumentException("Missing "+key); }
if (!args.Contains("--isolated-bench")) throw new ArgumentException("Developer tool requires --isolated-bench.");
string output=Path.GetFullPath(Get("--output")); Directory.CreateDirectory(output);
string adapter=Get("--adapter");
IObdTransport? link=null;
try
{
    if(adapter=="vlinker") { var v=new VLinkerReadTransport(new FtdiSerialConnection(Get("--port"),115200)); link=v; v.Initialize(); }
    else if(adapter=="tactrix")
    {
        var drivers = J2534Drivers.Discover(J2534AdapterKind.Tactrix)
            .Where(driver => string.Equals(Path.GetFileName(driver.LibraryPath), "op20pt32.dll", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if(drivers.Length!=1) throw new IOException("Select one physical Tactrix registration before testing.");
        var t=new J2534ReadTransport(new NativeJ2534Channel(drivers[0])); link=t; t.Initialize();
    }
    else throw new ArgumentException("Adapter must be vlinker or tactrix.");
    var identity=ReadWorkflow.Identify(link);
    var workflow=new ObdDiagnostics(link,identity,ObdDescriptions.From(TuningDefinition.Known,identity.Calibration));
    var scan=workflow.Scan(); ObdReports.SaveNew(Path.Combine(output,"scan.json"),scan);
    if(args.Contains("--clear"))
    {
        var clear=workflow.Clear(output,true);
        File.WriteAllText(Path.Combine(output,"clear.json"),JsonSerializer.Serialize(clear,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine(JsonSerializer.Serialize(new{adapter,clear.Acknowledged,clear.OutcomeUncertain,clear.Detail,clear.BeforeReport,clear.ConnectionUsable}));
        if(!clear.Acknowledged || !clear.ConnectionUsable) Environment.ExitCode=1;
    }
    else { Console.WriteLine(JsonSerializer.Serialize(new{adapter,scan.ConnectionUsable,stored=scan.Stored.Status,pending=scan.Pending.Status,permanent=scan.Permanent.Status,freeze=scan.FreezeFrame.Status,storedCount=scan.Stored.Value?.Count,pendingCount=scan.Pending.Value?.Count})); if(!scan.ConnectionUsable) Environment.ExitCode=1; }
}
catch(Exception e) { File.WriteAllText(Path.Combine(output,"error.json"),JsonSerializer.Serialize(new{error=e.ToString()})); Console.Error.WriteLine(e.Message); Environment.ExitCode=1; }
finally { link?.Dispose(); }
