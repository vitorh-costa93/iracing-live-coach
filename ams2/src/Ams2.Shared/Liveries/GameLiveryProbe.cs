using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Ams2.Shared.Liveries;

/// <summary>
/// Le da memoria do processo do jogo a pintura que o jogador escolheu. O AMS2 nao a expoe na memoria compartilhada, mas o heap guarda um registro
/// <c>{pasta do modelo}{hash 4B}{u32}{u32 tamanho}{nome da pintura}</c> (ex.: "formula_v8_g1_b" + ... + "Michael Schumacher #5") por pintura em uso.
/// Somente leitura (PROCESS_VM_READ). Varre ~10 GB (~15 s): chamar so fora da thread de dados e raramente.
/// </summary>
public static class GameLiveryProbe
{
    static readonly string[] ProcessNames = ["AMS2AVX", "AMS2"];

    /// <summary>Le o nome da pintura que segue a pasta em <paramref name="buf"/> (a pasta termina em <paramref name="folderEnd"/>); null se nao parecer um registro.</summary>
    public static string? ParseRecord(ReadOnlySpan<byte> buf, int folderEnd)
    {
        int o = folderEnd + 8; // hash (4) + u32 (4)
        if (o + 4 > buf.Length) return null;
        int len = BitConverter.ToInt32(buf.Slice(o, 4));
        if (len is <= 3 or >= 80 || o + 4 + len > buf.Length) return null;
        var raw = buf.Slice(o + 4, len);
        foreach (byte b in raw) if (b < 32 || b >= 127) return null;
        return Encoding.ASCII.GetString(raw);
    }

    /// <summary>Pinturas encontradas (nome -> ocorrencias) para as <paramref name="folders"/> dadas, ou null se o jogo nao esta aberto.
    /// <paramref name="known"/> descarta lixo: so nomes do catalogo contam.</summary>
    public static Dictionary<string, int>? Scan(IReadOnlyCollection<string> folders, Func<string, bool> known, CancellationToken ct = default)
    {
        Process? proc = null;
        foreach (var n in ProcessNames) { proc = Process.GetProcessesByName(n).FirstOrDefault(); if (proc is not null) break; }
        if (proc is null || folders.Count == 0) return null;
        var pats = folders.Select(f => Encoding.ASCII.GetBytes(f)).Where(p => p.Length > 0).ToArray();
        nint h = OpenProcess(0x0410, false, proc.Id); // VM_READ | QUERY_INFORMATION
        if (h == 0) return null;
        var found = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var buf = new byte[1 << 22];
            const int overlap = 256;
            long addr = 0;
            while (!ct.IsCancellationRequested && VirtualQueryEx(h, (nint)addr, out var mbi, (nuint)Marshal.SizeOf<Mbi>()) != 0)
            {
                long size = (long)mbi.RegionSize;
                if (mbi.State == 0x1000 && mbi.Type == 0x20000 && mbi.Protect == 0x04) // MEM_COMMIT, MEM_PRIVATE, PAGE_READWRITE (heap)
                    for (long off = 0; off < size; off += buf.Length - overlap)
                    {
                        int n = (int)Math.Min(buf.Length, size - off);
                        if (!ReadProcessMemory(h, (nint)((long)mbi.BaseAddress + off), buf, n, out int rd) && rd == 0) break;
                        foreach (var p in pats)
                        {
                            int i = 0;
                            while (i < rd)
                            {
                                int k = buf.AsSpan(i, rd - i).IndexOf(p);
                                if (k < 0) break;
                                i += k;
                                var name = ParseRecord(buf.AsSpan(0, rd), i + p.Length);
                                if (name is not null && known(name)) found[name] = found.GetValueOrDefault(name) + 1;
                                i += p.Length;
                            }
                        }
                        if (off + n >= size) break;
                    }
                addr = (long)mbi.BaseAddress + size;
            }
        }
        finally { CloseHandle(h); }
        return found;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct Mbi { public nint BaseAddress, AllocationBase; public uint AllocationProtect; public ushort Pad1, Pad2; public nuint RegionSize; public uint State, Protect, Type, Pad3; }
    [DllImport("kernel32")] static extern nint OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32")] static extern bool CloseHandle(nint h);
    [DllImport("kernel32")] static extern int VirtualQueryEx(nint h, nint addr, out Mbi m, nuint len);
    [DllImport("kernel32")] static extern bool ReadProcessMemory(nint h, nint addr, byte[] buf, int n, out int read);
}
