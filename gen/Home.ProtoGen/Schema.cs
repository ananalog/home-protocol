using System.Globalization;
using YamlDotNet.RepresentationModel;

namespace Home.ProtoGen;

public enum Scalar { U8, U16, U32, U64, I32, I64, F32, Bool, Str, Bytes }

/// <summary>Type of a field: a scalar, a struct reference, optionally wrapped in list&lt;&gt;.</summary>
public sealed record FieldType(bool IsList, Scalar? Scalar, string? Struct)
{
    public bool IsStruct => Struct != null;

    public static FieldType Parse(string text, ISet<string> structs)
    {
        var t = text.Trim();
        var isList = false;
        if (t.StartsWith("list<") && t.EndsWith(">"))
        {
            isList = true;
            t = t[5..^1].Trim();
        }
        return t switch
        {
            "u8" => new(isList, Home.ProtoGen.Scalar.U8, null),
            "u16" => new(isList, Home.ProtoGen.Scalar.U16, null),
            "u32" => new(isList, Home.ProtoGen.Scalar.U32, null),
            "u64" => new(isList, Home.ProtoGen.Scalar.U64, null),
            "i32" => new(isList, Home.ProtoGen.Scalar.I32, null),
            "i64" => new(isList, Home.ProtoGen.Scalar.I64, null),
            "f32" => new(isList, Home.ProtoGen.Scalar.F32, null),
            "bool" => new(isList, Home.ProtoGen.Scalar.Bool, null),
            "str" => new(isList, Home.ProtoGen.Scalar.Str, null),
            "bytes" => new(isList, Home.ProtoGen.Scalar.Bytes, null),
            _ when structs.Contains(t) => new(isList, null, t),
            _ => throw new InvalidDataException($"unknown type '{text}'"),
        };
    }
}

public sealed record Field(int Tag, string Name, FieldType Type, int Max, int ItemMax, bool Critical, string? Enum)
{
    public int WireTag => Critical ? Tag | 0x80 : Tag;
}

public sealed record StructDef(string Name, List<Field> Fields);

public sealed record MessageDef(string Name, int Id, string Dir, StructDef Req, StructDef? Resp);

public sealed record EnumDef(string Name, List<(string Name, long Value)> Values, bool IsFlags);

public sealed class Schema
{
    public int Major { get; private set; }
    public int Minor { get; private set; }
    public List<(string Name, object Value)> Constants { get; } = new();
    public List<EnumDef> Enums { get; } = new();
    public List<StructDef> Structs { get; } = new();
    public List<MessageDef> Messages { get; } = new();

    public EnumDef? FindEnum(string name) => Enums.FirstOrDefault(e => e.Name == name);

    /// <summary>Structs in dependency order (a struct comes after the structs it uses).</summary>
    public IEnumerable<StructDef> AllBodies()
    {
        foreach (var s in Structs) yield return s;
        foreach (var m in Messages)
        {
            yield return m.Req;
            if (m.Resp != null) yield return m.Resp;
        }
    }

    public static Schema Load(string path)
    {
        var yaml = new YamlStream();
        using (var reader = File.OpenText(path)) yaml.Load(reader);
        var root = (YamlMappingNode)yaml.Documents[0].RootNode;
        var s = new Schema();

        var ver = (YamlMappingNode)root["version"];
        s.Major = Int(ver["major"]);
        s.Minor = Int(ver["minor"]);

        foreach (var (k, v) in (YamlMappingNode)root["constants"])
        {
            var sv = (YamlScalarNode)v;
            object value = sv.Style is YamlDotNet.Core.ScalarStyle.DoubleQuoted or YamlDotNet.Core.ScalarStyle.SingleQuoted
                ? sv.Value!
                : ParseLong(sv.Value!);
            s.Constants.Add((((YamlScalarNode)k).Value!, value));
        }

        foreach (var (k, v) in (YamlMappingNode)root["enums"])
        {
            var name = ((YamlScalarNode)k).Value!;
            var values = ((YamlMappingNode)v).Select(p => (((YamlScalarNode)p.Key).Value!, ParseLong(((YamlScalarNode)p.Value).Value!))).ToList();
            s.Enums.Add(new EnumDef(name, values, name.EndsWith("Flags")));
        }

        var structsNode = (YamlMappingNode)root["structs"];
        var structNames = structsNode.Select(p => ((YamlScalarNode)p.Key).Value!).ToHashSet();
        foreach (var (k, v) in structsNode)
        {
            var name = ((YamlScalarNode)k).Value!;
            s.Structs.Add(new StructDef(name, ParseFields((YamlSequenceNode)v, structNames, s, name)));
        }

        foreach (var (k, v) in (YamlMappingNode)root["messages"])
        {
            var name = ((YamlScalarNode)k).Value!;
            var m = (YamlMappingNode)v;
            var id = Int(m["id"]);
            var dir = ((YamlScalarNode)m["dir"]).Value!;
            var reqNode = m.Children.TryGetValue("req", out var r) ? r as YamlSequenceNode : null;
            var respNode = m.Children.TryGetValue("resp", out var rs) ? rs as YamlSequenceNode : null;
            var pascal = Names.Pascal(name);
            var req = new StructDef(pascal + "Req", reqNode == null ? new() : ParseFields(reqNode, structNames, s, name));
            var resp = m.Children.ContainsKey("resp")
                ? new StructDef(pascal + "Resp", respNode == null ? new() : ParseFields(respNode, structNames, s, name))
                : null;
            s.Messages.Add(new MessageDef(name, id, dir, req, resp));
        }

        s.Validate();
        return s;
    }

    private static List<Field> ParseFields(YamlSequenceNode seq, ISet<string> structs, Schema s, string owner)
    {
        var list = new List<Field>();
        foreach (var node in seq.Cast<YamlMappingNode>())
        {
            string Get(string key) => node.Children.TryGetValue(key, out var n) ? ((YamlScalarNode)n).Value! : "";
            var type = FieldType.Parse(Get("type"), structs);
            var max = Get("max") is { Length: > 0 } m ? int.Parse(m, CultureInfo.InvariantCulture) : 0;
            var itemMax = Get("item_max") is { Length: > 0 } im ? int.Parse(im, CultureInfo.InvariantCulture) : 0;
            var enumName = Get("enum") is { Length: > 0 } e ? e : null;
            var f = new Field(Int(node["tag"]), Get("name"), type, max, itemMax, Get("critical") == "true", enumName);
            if ((type.Scalar is Scalar.Str or Scalar.Bytes) && !type.IsList && f.Max == 0)
                throw new InvalidDataException($"{owner}.{f.Name}: str/bytes needs max");
            if (type.IsList && f.Max == 0)
                throw new InvalidDataException($"{owner}.{f.Name}: list needs max");
            if (type.IsList && type.Scalar == Scalar.Str && f.ItemMax == 0)
                throw new InvalidDataException($"{owner}.{f.Name}: list<str> needs item_max");
            list.Add(f);
        }
        return list;
    }

    private void Validate()
    {
        foreach (var body in AllBodies())
        {
            var tags = new HashSet<int>();
            var prev = 0;
            foreach (var f in body.Fields)
            {
                if (f.Tag is < 1 or > 127) throw new InvalidDataException($"{body.Name}.{f.Name}: tag out of range");
                if (!tags.Add(f.Tag)) throw new InvalidDataException($"{body.Name}.{f.Name}: duplicate tag");
                if (f.Tag <= prev) throw new InvalidDataException($"{body.Name}.{f.Name}: tags must be ascending");
                prev = f.Tag;
                if (f.Enum != null && FindEnum(f.Enum) == null) throw new InvalidDataException($"{body.Name}.{f.Name}: unknown enum {f.Enum}");
            }
        }
        var ids = new HashSet<int>();
        foreach (var m in Messages)
            if (!ids.Add(m.Id)) throw new InvalidDataException($"duplicate message id {m.Id}");
    }

    private static int Int(YamlNode n) => (int)ParseLong(((YamlScalarNode)n).Value!);

    public static long ParseLong(string v) =>
        v.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? long.Parse(v[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture)
            : long.Parse(v, CultureInfo.InvariantCulture);
}

public static class Names
{
    /// <summary>HELLO_WORLD / hello_world / HelloWorld -> HelloWorld.</summary>
    public static string Pascal(string s)
    {
        if (!s.Contains('_') && s.Any(char.IsLower)) return char.ToUpperInvariant(s[0]) + s[1..];
        return string.Concat(s.ToLowerInvariant().Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => char.ToUpperInvariant(p[0]) + p[1..]));
    }

    public static string Camel(string s)
    {
        var p = Pascal(s);
        return char.ToLowerInvariant(p[0]) + p[1..];
    }

    /// <summary>HelloReq / HELLO / PointDef -> hello_req / hello / point_def.</summary>
    public static string Snake(string s)
    {
        if (s.All(c => !char.IsLower(c))) return s.ToLowerInvariant();
        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (char.IsUpper(c) && i > 0 && (char.IsLower(s[i - 1]) || (i + 1 < s.Length && char.IsLower(s[i + 1]))))
                sb.Append('_');
            sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }

    public static string UpperSnake(string s) => Snake(s).ToUpperInvariant();
}
