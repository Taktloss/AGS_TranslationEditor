using System.Text;

namespace AGSTools.Tests;

/// <summary>Writes a compiled AGS script (SCOM) with a small instruction set for tests.</summary>
internal sealed class ScriptBuilder
{
    private const int LitToReg = 6, CallExt = 33, PushReal = 34, SubRealStack = 35, LineNum = 36, CallObj = 45;
    private const int MAR = 2, AX = 3;
    private const byte FixupString = 3, FixupImport = 4;

    private readonly List<int> _code = new();
    private readonly List<(byte Type, int Address)> _fixups = new();
    private readonly MemoryStream _strings = new();
    private readonly List<string> _imports = new();
    private readonly List<(string Name, int Offset)> _sections = new();

    public ScriptBuilder Section(string name)
    {
        _sections.Add((name, _code.Count));
        return this;
    }

    public ScriptBuilder Line(int number) => Emit(LineNum, number);

    /// <summary>character.Say(text) as the compiler emits it for an imported character.</summary>
    public ScriptBuilder Say(string character, string text)
    {
        LoadString(text);
        Emit(PushReal, AX);
        LoadImport(MAR, character);
        Emit(CallObj, MAR);
        LoadImport(AX, "Character::Say^1");
        Emit(CallExt, AX);
        return Emit(SubRealStack, 1);
    }

    /// <summary>A global function call with a text and optional integer arguments before it, e.g. DisplaySpeech(0, text).</summary>
    public ScriptBuilder Call(string function, string text, params int[] numbers)
    {
        LoadString(text);
        Emit(PushReal, AX);
        foreach (int number in numbers)
        {
            Emit(LitToReg, AX, number);
            Emit(PushReal, AX);
        }
        LoadImport(AX, function);
        Emit(CallExt, AX);
        return Emit(SubRealStack, 1 + numbers.Length);
    }

    /// <summary>String.Format(format, ...) with the format literal, as the compiler emits it (the parts come from variables).</summary>
    public ScriptBuilder Format(string format) => Call("String::Format^101", format);

    /// <summary>A string used without a call, e.g. assigned to a variable.</summary>
    public ScriptBuilder Literal(string text)
    {
        LoadString(text);
        return this;
    }

    private void LoadString(string text)
    {
        int offset = (int)_strings.Length;
        _strings.Write(Encoding.Latin1.GetBytes(text + "\0"));
        _fixups.Add((FixupString, _code.Count + 2));
        Emit(LitToReg, AX, offset);
    }

    private void LoadImport(int register, string name)
    {
        int index = _imports.IndexOf(name);
        if (index < 0)
        {
            index = _imports.Count;
            _imports.Add(name);
        }
        _fixups.Add((FixupImport, _code.Count + 2));
        Emit(LitToReg, register, index);
    }

    private ScriptBuilder Emit(int op, params int[] args)
    {
        _code.Add(op);
        _code.AddRange(args);
        return this;
    }

    public byte[] Build()
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms, Encoding.Latin1);
        w.Write("SCOM"u8.ToArray());
        w.Write(89); // version with sections
        w.Write(0); // global data
        w.Write(_code.Count);
        w.Write((int)_strings.Length);
        foreach (int word in _code) w.Write(word);
        w.Write(_strings.ToArray());
        w.Write(_fixups.Count);
        foreach (var (type, _) in _fixups) w.Write(type);
        foreach (var (_, address) in _fixups) w.Write(address);
        w.Write(_imports.Count);
        foreach (string import in _imports) w.Write(Encoding.ASCII.GetBytes(import + "\0"));
        w.Write(0); // exports
        var sections = _sections.Count > 0 ? _sections : new List<(string, int)> { ("GlobalScript.asc", 0) };
        w.Write(sections.Count);
        foreach (var (name, offset) in sections)
        {
            w.Write(Encoding.ASCII.GetBytes(name + "\0"));
            w.Write(offset);
        }
        w.Write(0xbeefcafeu);
        return ms.ToArray();
    }
}
