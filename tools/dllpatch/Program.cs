// Mono DLL 문자열 치환: ldstr / 매개변수 기본값 / const 필드.
// usage: dllpatch <원본 Managed 폴더> <출력 폴더> <rules.json>
// rules.json: [{"asm":"Garden.ViewModels.dll","type":"Garden.MusicPlaylistViewModel"(생략=전체),"old":"…","new":"…","min":1}]
// 각 규칙은 최소 min(기본 1)번 바뀌어야 한다. 아니면 실패(게임 업데이트로 문자열이 바뀐 경우를 잡기 위해).
using System.Text.Json;
using Mono.Cecil;
using Mono.Cecil.Cil;

Console.OutputEncoding = System.Text.Encoding.UTF8;
var src = args[0]; var dst = args[1];
var rules = JsonSerializer.Deserialize<List<Rule>>(File.ReadAllText(args[2]), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
Directory.CreateDirectory(dst);
var resolver = new DefaultAssemblyResolver();
resolver.AddSearchDirectory(src);
bool ok = true;
foreach (var grp in rules.GroupBy(r => r.Asm))
{
    var path = Path.Combine(src, grp.Key);
    var asm = AssemblyDefinition.ReadAssembly(path, new ReaderParameters { AssemblyResolver = resolver, ReadingMode = ReadingMode.Immediate });
    var hits = grp.ToDictionary(r => r, r => 0);
    foreach (var type in asm.MainModule.GetTypes())
    {
        var applicable = grp.Where(r => string.IsNullOrEmpty(r.Type) || type.FullName == r.Type || type.FullName.StartsWith(r.Type + "/")).ToList();
        if (applicable.Count == 0) continue;
        foreach (var f in type.Fields)
            if (f.HasConstant && f.Constant is string fs)
                foreach (var r in applicable) if (fs == r.Old) { f.Constant = r.New; hits[r]++; }
        foreach (var m in type.Methods)
        {
            foreach (var p in m.Parameters)
                if (p.HasConstant && p.Constant is string ps)
                    foreach (var r in applicable) if (ps == r.Old) { p.Constant = r.New; hits[r]++; }
            if (!m.HasBody) continue;
            foreach (var ins in m.Body.Instructions)
                if (ins.OpCode == OpCodes.Ldstr && ins.Operand is string s)
                    foreach (var r in applicable) if (s == r.Old) { ins.Operand = r.New; hits[r]++; break; }
        }
    }
    foreach (var (r, n) in hits)
    {
        Console.WriteLine($"{(n >= r.Min ? "ok  " : "FAIL")} {grp.Key} {r.Type ?? "*"} x{n}  {Show(r.Old)} -> {Show(r.New)}");
        if (n < r.Min) ok = false;
    }
    if (hits.Values.Sum() > 0) asm.Write(Path.Combine(dst, grp.Key));
    else Console.WriteLine($"     {grp.Key} 변경 없음 → 쓰지 않음");
}
return ok ? 0 : 1;

static string Show(string s) => s.Replace("\n", "\\n");

class Rule { public string Asm { get; set; } public string Type { get; set; } public string Old { get; set; } public string New { get; set; } public int Min { get; set; } = 1; }
