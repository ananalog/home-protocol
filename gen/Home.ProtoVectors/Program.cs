using System.Text.Json;
using System.Text.Json.Nodes;
using Home.Protocol;

// Encodes testdata/vectors.json into testdata/vectors/*.bin + index.txt.
// Every implementation (C, C#, Kotlin) must decode each .bin and re-encode it to the same bytes.
var root = args.Length > 0 ? args[0] : ".";
var json = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "testdata/vectors.json")))!.AsArray();
var outDir = Path.Combine(root, "testdata/vectors");
Directory.CreateDirectory(outDir);
foreach (var f in Directory.GetFiles(outDir, "*.bin")) File.Delete(f);

var opts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, IncludeFields = false };
var index = new List<string>();
foreach (var v in json)
{
    var name = v!["name"]!.GetValue<string>();
    var typeName = v["type"]!.GetValue<string>();
    var kind = v["kind"]?.GetValue<string>() ?? "req"; // req | resp | err
    var reqId = v["req_id"]?.GetValue<int>() ?? 0;
    var type = Enum.Parse<MsgType>(string.Concat(typeName.ToLowerInvariant().Split('_').Select(p => char.ToUpperInvariant(p[0]) + p[1..])));
    var className = kind == "err" ? "ErrorBody" : type + (kind == "resp" ? "Resp" : "Req");
    var clr = typeof(Codec).Assembly.GetType("Home.Protocol." + className) ?? throw new Exception($"no class {className}");
    var body = (IProtoBody)(v["body"]?.Deserialize(clr, opts) ?? Activator.CreateInstance(clr)!);
    var flags = kind switch { "resp" => Flags.Resp, "err" => Flags.Resp | Flags.Err, _ => (Flags)0 };
    if (v["noack"]?.GetValue<bool>() == true) flags |= Flags.Noack;
    var bytes = Codec.Encode(type, flags, (ushort)reqId, body);
    File.WriteAllBytes(Path.Combine(outDir, name + ".bin"), bytes);
    index.Add($"{name} {(byte)type} {kind}");
    Console.WriteLine($"{name,-24} {bytes.Length,5} bytes");
}
File.WriteAllText(Path.Combine(outDir, "index.txt"), string.Join("\n", index) + "\n");
