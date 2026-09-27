using Home.ProtoGen;

// Usage: dotnet run --project gen/Home.ProtoGen -- [repo-root]
// Regenerates all language bindings from schema.yaml.
var root = Path.GetFullPath(args.Length > 0 ? args[0] : FindRoot());
var schema = Schema.Load(Path.Combine(root, "schema.yaml"));

void Write(string rel, string content)
{
    var path = Path.Combine(root, rel);
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    content = content.Replace("\r\n", "\n");
    if (File.Exists(path) && File.ReadAllText(path) == content) return;
    File.WriteAllText(path, content);
    Console.WriteLine($"wrote {rel}");
}

var (h, c) = CEmitter.Emit(schema);
Write("c/include/home_proto.h", h);
Write("c/src/home_proto.c", c);
Write("tests/c/roundtrip.gen.c", CEmitter.EmitRoundtrip(schema));
Write("csharp/Home.Protocol/Generated/Proto.g.cs", CSharpEmitter.Emit(schema));
Write("kotlin/src/main/kotlin/home/protocol/Proto.kt", KotlinEmitter.Emit(schema));
Write("ts/home_proto.ts", TsEmitter.Emit(schema));
Console.WriteLine($"protocol v{schema.Major}.{schema.Minor}: {schema.Messages.Count} messages, {schema.Structs.Count} structs, {schema.Enums.Count} enums");

static string FindRoot()
{
    var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (dir != null && !File.Exists(Path.Combine(dir.FullName, "schema.yaml"))) dir = dir.Parent;
    return dir?.FullName ?? throw new InvalidOperationException("schema.yaml not found; pass the repo root");
}
