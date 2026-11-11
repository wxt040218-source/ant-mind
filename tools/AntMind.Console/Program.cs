using AntMind.Transport;
var client=new PipeClient(Environment.GetEnvironmentVariable("ANTMIND_PIPE")??"antmind-v1");
var console=new CommandConsole(client.SendAsync);
using var cancellation=new CancellationTokenSource();
System.Console.CancelKeyPress+=(_,e)=>{e.Cancel=true;cancellation.Cancel();};
if(args.Length>0) {
    var result=await console.ExecuteAsync(string.Join(" ",args),cancellation.Token);
    System.Console.WriteLine(result);
    if(result.StartsWith("error:",StringComparison.Ordinal) || result.StartsWith("usage:",StringComparison.Ordinal))Environment.ExitCode=1;
    else {try{var root=System.Text.Json.JsonDocument.Parse(result).RootElement;if(root.ValueKind==System.Text.Json.JsonValueKind.Object && (root.TryGetProperty("Accepted",out var accepted) && accepted.ValueKind==System.Text.Json.JsonValueKind.False || root.TryGetProperty("Error",out var error) && error.ValueKind!=System.Text.Json.JsonValueKind.Null))Environment.ExitCode=1;}catch(System.Text.Json.JsonException){}}
    return;
}
System.Console.WriteLine("Ant Mind local console. help lists commands; exit closes this client.");
while(!cancellation.IsCancellationRequested) {
    System.Console.Write("antmind> ");var line=System.Console.ReadLine();
    if(line is null || line.Trim().Equals("exit",StringComparison.OrdinalIgnoreCase))break;
    try {System.Console.WriteLine(await console.ExecuteAsync(line,cancellation.Token));}
    catch(OperationCanceledException){break;}
}
