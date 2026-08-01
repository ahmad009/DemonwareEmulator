using System.Collections.Concurrent;

namespace Demonware.Core.Store;

/// <summary>File-backed persistence under ./data — no external database.</summary>
public sealed class FileStore
{
    private readonly string _root;
    private readonly object _gate = new();

    public FileStore(string? root = null)
    {
        _root = Path.GetFullPath(root ?? "data");
        foreach (var sub in new[] { "pub", "files", "profiles", "keys", "events", "fileshare" })
            Directory.CreateDirectory(Path.Combine(_root, sub));
    }

    public string Root => _root;
    public string PubDir => Path.Combine(_root, "pub");
    public string FileshareDir => Path.Combine(_root, "fileshare");

    private static string Safe(string name)
    {
        if (string.IsNullOrEmpty(name)) return "_empty";
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c));
    }

    public void SaveFile(string key, int userId, byte[] data)
    {
        data ??= [];
        var path = Path.Combine(_root, "files", Safe(key) + ".bin");
        lock (_gate)
        {
            using var fs = File.Create(path);
            using var bw = new BinaryWriter(fs);
            bw.Write(userId);
            bw.Write(data.Length);
            bw.Write(data);
        }
    }

    public byte[]? GetFile(string key)
    {
        var path = Path.Combine(_root, "files", Safe(key) + ".bin");
        lock (_gate)
        {
            if (!File.Exists(path)) return null;
            using var fs = File.OpenRead(path);
            using var br = new BinaryReader(fs);
            _ = br.ReadInt32();
            var len = br.ReadInt32();
            return br.ReadBytes(len);
        }
    }

    public void SaveProfile(int userId, int profileInt, byte[] blob)
    {
        blob ??= [];
        var path = Path.Combine(_root, "profiles", userId.ToString("x8") + ".bin");
        lock (_gate)
        {
            using var fs = File.Create(path);
            using var bw = new BinaryWriter(fs);
            bw.Write(userId);
            bw.Write(profileInt);
            bw.Write(blob.Length);
            bw.Write(blob);
        }
    }

    public bool TryGetProfile(int userId, out int profileInt, out byte[] blob)
    {
        profileInt = 0;
        blob = [];
        var path = Path.Combine(_root, "profiles", userId.ToString("x8") + ".bin");
        lock (_gate)
        {
            if (!File.Exists(path)) return false;
            using var fs = File.OpenRead(path);
            using var br = new BinaryReader(fs);
            _ = br.ReadInt32();
            profileInt = br.ReadInt32();
            blob = br.ReadBytes(br.ReadInt32());
            return true;
        }
    }

    public void SaveServerKey(long keyHash, string key, int unk)
    {
        var path = Path.Combine(_root, "keys", keyHash.ToString("x16") + ".txt");
        lock (_gate) File.WriteAllText(path, key + "\n" + unk, System.Text.Encoding.ASCII);
    }

    public bool TryGetServerKey(long keyHash, out string key, out int unk)
    {
        key = "";
        unk = 0;
        var path = Path.Combine(_root, "keys", keyHash.ToString("x16") + ".txt");
        lock (_gate)
        {
            if (!File.Exists(path)) return false;
            var lines = File.ReadAllLines(path, System.Text.Encoding.ASCII);
            if (lines.Length < 1) return false;
            key = lines[0];
            if (lines.Length >= 2) int.TryParse(lines[1], out unk);
            return true;
        }
    }

    public void AppendEvent(int type, byte[] data)
    {
        data ??= [];
        var name = $"{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}_{type}.bin";
        try { File.WriteAllBytes(Path.Combine(_root, "events", name), data); }
        catch { /* ignore */ }
    }
}

/// <summary>Per-connection session crypto keys for the legacy 3DES path.</summary>
public sealed class SessionKeyMap
{
    private readonly ConcurrentDictionary<string, byte[]> _keys = new(StringComparer.Ordinal);

    public void Set(string connectionId, byte[] key24)
    {
        var copy = new byte[24];
        Array.Copy(key24, copy, 24);
        _keys[connectionId] = copy;
    }

    public byte[] Get(string connectionId) =>
        _keys.TryGetValue(connectionId, out var k) ? k : new byte[24];

    public void Remove(string connectionId) => _keys.TryRemove(connectionId, out _);
}
