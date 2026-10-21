using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace SpeedLink.Core;

public sealed record TuningAxis(string Name, double[] Values);
public sealed record TuningState(string Name, ulong Value);

/// <summary>ROM storage is row-major: X varies fastest. Static axes are display metadata, never writes.</summary>
public sealed class TuningTable
{
    public required string Name { get; init; }
    public required string Category { get; init; }
    public required string Kind { get; init; }
    public required string Storage { get; init; }
    public required string Units { get; init; }
    public required string Description { get; init; }
    public required string Format { get; init; }
    public required int Address { get; init; }
    public required int Columns { get; init; }
    public required int Rows { get; init; }
    public required bool BigEndian { get; init; }
    public int[] Bits { get; init; } = [];
    public string[] BitNames { get; init; } = [];
    public int? Bit => Bits.Length == 0 ? null : Bits[0];
    public required Func<double, double> Decode { get; init; }
    public required Func<double, double> Encode { get; init; }
    public required TuningAxis X { get; init; }
    public required TuningAxis Y { get; init; }
    public TuningState[] States { get; init; } = [];
    public int Width => Storage switch { "float" or "uint32" or "int32" => 4, "uint16" or "int16" => 2, "uint8" or "int8" => 1, _ => throw new InvalidDataException("Unsupported storage.") };
    public int Count => checked(Columns * Rows);
    public int End => checked(Address + Width * (Bits.Length > 0 ? 1 : Count));
    public int AddressAt(int index) => Address + (Bits.Length > 0 ? 0 : index * Width);
    public bool IsChoice => Kind is "Switch" or "BitwiseSwitch";
    public string Display(double value) => double.IsFinite(value) ? value.ToString("G10", CultureInfo.InvariantCulture) : "Invalid";
}

public sealed class TuningDefinition
{
    public required string Calibration { get; init; }
    public required string Model { get; init; }
    public required string Version { get; init; }
    public required int IdentityAddress { get; init; }
    public required string Identity { get; init; }
    public required IReadOnlyList<TuningTable> Tables { get; init; }
    public static IReadOnlyList<TuningDefinition> Known { get; } = LoadKnown();
    public bool Matches(ReadOnlySpan<byte> image) => image.Length == RomInspector.RomSize &&
        IdentityAddress >= 0 && IdentityAddress + Identity.Length <= image.Length &&
        image.Slice(IdentityAddress, Identity.Length).SequenceEqual(Encoding.ASCII.GetBytes(Identity));
    private static IReadOnlyList<TuningDefinition> LoadKnown()
    {
        var assembly = typeof(TuningDefinition).Assembly;
        return Array.AsReadOnly(assembly.GetManifestResourceNames().Where(n => n.StartsWith("SpeedLink.Core.Tuning.", StringComparison.Ordinal)).Order().Select(n =>
        {
            using var stream = assembly.GetManifestResourceStream(n)!;
            return Parse(stream);
        }).ToArray());
    }
    public static TuningDefinition Parse(Stream input)
    {
        using var reader = XmlReader.Create(input, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 8_000_000 });
        var rom = XDocument.Load(reader).Root?.Elements("rom").Single() ?? throw new InvalidDataException("One ROM definition required.");
        var id = rom.Element("romid") ?? throw new InvalidDataException("ROM identity missing.");
        string Text(string name) => id.Element(name)?.Value ?? throw new InvalidDataException("Missing " + name);
        static string Attr(XElement node, string name) => node.Attribute(name)?.Value ?? throw new InvalidDataException("Missing " + name);
        static int Address(string text) => int.Parse(text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? text[2..] : text, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        static double Number(string text) => double.Parse(text, CultureInfo.InvariantCulture);
        static TuningAxis Axis(XElement node, string type, int count)
        {
            var axis = node.Elements("table").SingleOrDefault(t => (string?)t.Attribute("type") == type);
            if (axis is null)
            {
                if (count != 1) throw new InvalidDataException("Missing static axis.");
                return new("", [0]);
            }
            var values = axis.Elements("data").Select(d => Number(d.Value)).ToArray();
            if (values.Length != count || values.Any(v => !double.IsFinite(v))) throw new InvalidDataException("Axis dimensions do not match.");
            return new((string?)axis.Attribute("name") ?? "Axis", values);
        }
        var tables = rom.Elements("table").Select(node =>
        {
            var kind = Attr(node, "type");
            if (kind is not ("1D" or "2D" or "3D" or "Switch" or "BitwiseSwitch")) throw new InvalidDataException("Unsupported table type: " + kind);
            var scaling = node.Element("scaling") ?? throw new InvalidDataException("Missing scaling.");
            var bits = node.Elements("bit").Select(b => int.Parse(Attr(b, "position"), CultureInfo.InvariantCulture)).ToArray();
            var columns = kind == "BitwiseSwitch" ? 1 : int.Parse(Attr(node, "sizex"), CultureInfo.InvariantCulture);
            var rows = kind == "BitwiseSwitch" ? bits.Length : int.Parse((string?)node.Attribute("sizey") ?? "1", CultureInfo.InvariantCulture);
            var endian = Attr(node, "endian");
            if (endian is not ("big" or "little")) throw new InvalidDataException("Invalid byte order.");
            var result = new TuningTable
            {
                Name = Attr(node, "name"), Category = Attr(node, "category"), Kind = kind,
                Storage = Attr(node, "storagetype"), Address = Address(Attr(node, "storageaddress")),
                Columns = columns, Rows = rows, BigEndian = endian == "big",
                Units = (string?)scaling.Attribute("units") ?? "", Format = (string?)scaling.Attribute("format") ?? "0.####",
                Description = node.Element("description")?.Value ?? "",
                Decode = ScalingExpression.Compile(Attr(scaling, "expression")), Encode = ScalingExpression.Compile(Attr(scaling, "to_byte")),
                Bits = bits, BitNames = node.Elements("bit").Select(b => Attr(b, "name")).ToArray(),
                States = node.Elements("state").Select(s => new TuningState(Attr(s, "name"), ulong.Parse(Attr(s, "data"), NumberStyles.HexNumber, CultureInfo.InvariantCulture))).ToArray(),
                X = Axis(node, "Static X Axis", columns), Y = kind == "BitwiseSwitch" ? new("Control", Enumerable.Range(0, rows).Select(i => (double)i).ToArray()) : Axis(node, "Static Y Axis", rows)
            };
            if (columns < 1 || rows < 1 || result.Count > 16384 || result.Address < RomInspector.BootSize || result.End > RomInspector.RomSize - 2 ||
                result.Bits.Any(b => b < 0 || b >= result.Width * 8) || result.Bits.Distinct().Count() != result.Bits.Length ||
                kind == "BitwiseSwitch" && result.Bit is null || kind == "Switch" && (result.Count != 1 || result.States.Length < 2))
                throw new InvalidDataException("Invalid tuning storage: " + result.Name);
            return result;
        }).OrderBy(t => t.Category, NaturalOrder.Instance).ThenBy(t => t.Name, NaturalOrder.Instance).ToArray();
        if (tables.Select(t => t.Name).Distinct().Count() != tables.Length) throw new InvalidDataException("Duplicate table names.");
        var identity = Text("internalidstring");
        return new() { Calibration = identity[..9], Identity = identity, IdentityAddress = Address(Text("internalidaddress")), Model = Text("model"), Version = Text("version"), Tables = Array.AsReadOnly(tables) };
    }
}

public sealed class NaturalOrder : IComparer<string>
{
    public static NaturalOrder Instance { get; } = new();
    public int Compare(string? a, string? b)
    {
        var x = Regex.Split(a ?? "", "([0-9]+)"); var y = Regex.Split(b ?? "", "([0-9]+)");
        for (int i = 0; i < Math.Min(x.Length, y.Length); i++)
        {
            int c = double.TryParse(x[i], NumberStyles.None, CultureInfo.InvariantCulture, out var n) && double.TryParse(y[i], NumberStyles.None, CultureInfo.InvariantCulture, out var m)
                ? n.CompareTo(m) : StringComparer.OrdinalIgnoreCase.Compare(x[i], y[i]);
            if (c != 0) return c;
        }
        return x.Length.CompareTo(y.Length);
    }
}

/// <summary>Small, non-executable expression grammar used by the bundled RomRaider definitions.</summary>
internal sealed class ScalingExpression(string expression)
{
    private int at;
    public static Func<double, double> Compile(string expression)
    {
        if (expression.Length > 1024) throw new InvalidDataException("Scaling too long.");
        var parser = new ScalingExpression(expression); var result = parser.Compare(); parser.Space();
        if (parser.at != expression.Length) throw new InvalidDataException("Invalid scaling: " + expression);
        return result;
    }
    private void Space() { while (at < expression.Length && char.IsWhiteSpace(expression[at])) at++; }
    private bool Take(char c) { Space(); if (at >= expression.Length || expression[at] != c) return false; at++; return true; }
    private void Need(char c) { if (!Take(c)) throw new InvalidDataException("Invalid scaling expression."); }
    private Func<double, double> Compare()
    {
        var left = Sum(); if (!Take('>')) return left; var right = Sum(); return x => left(x) > right(x) ? 1 : 0;
    }
    private Func<double, double> Sum()
    {
        var left = Product();
        while (true) { if (Take('+')) { var a = left; var b = Product(); left = x => a(x) + b(x); } else if (Take('-')) { var a = left; var b = Product(); left = x => a(x) - b(x); } else return left; }
    }
    private Func<double, double> Product()
    {
        var left = Atom();
        while (true) { if (Take('*')) { var a = left; var b = Atom(); left = x => a(x) * b(x); } else if (Take('/')) { var a = left; var b = Atom(); left = x => a(x) / b(x); } else return left; }
    }
    private Func<double, double> Atom()
    {
        if (Take('-')) { var value = Atom(); return x => -value(x); }
        if (Take('+')) return Atom();
        if (Take('(')) { var value = Compare(); Need(')'); return value; }
        Space();
        if (at < expression.Length && char.IsLetter(expression[at]))
        {
            int start = at; while (at < expression.Length && char.IsLetter(expression[at])) at++;
            string name = expression[start..at]; if (name == "x") return x => x;
            Need('('); var a = Compare();
            if (name == "abs") { Need(')'); return x => Math.Abs(a(x)); }
            Need(','); var b = Compare();
            if (name == "mod") { Need(')'); return x => a(x) % b(x); }
            if (name == "if") { Need(','); var c = Compare(); Need(')'); return x => a(x) != 0 ? b(x) : c(x); }
            throw new InvalidDataException("Unknown scaling function.");
        }
        int begin = at;
        while (at < expression.Length && (char.IsDigit(expression[at]) || expression[at] == '.')) at++;
        if (at < expression.Length && expression[at] is 'E' or 'e') { at++; if (at < expression.Length && expression[at] is '+' or '-') at++; while (at < expression.Length && char.IsDigit(expression[at])) at++; }
        if (!double.TryParse(expression[begin..at], NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number)) throw new InvalidDataException("Invalid scaling number.");
        return _ => number;
    }
}
