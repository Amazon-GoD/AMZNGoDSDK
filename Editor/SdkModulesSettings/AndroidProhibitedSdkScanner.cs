using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace AMZNGoDSDK.Editor
{
    /// <summary>Checks DEX type tables, including unresolved references from otherwise allowed SDKs.</summary>
    internal static class AndroidProhibitedSdkScanner
    {
        // Java package ownership, not arbitrary text matches. In particular, Branch is io.branch,
        // not a variable called "branch", and a network name in MAX's catalogue is not SDK code.
        private static string[][] Packages => ForbiddenAdNetworks.All
            .Where(network => network.DexTypePrefixes.Length != 0)
            .Select(network => new[] { network.DisplayName }.Concat(network.DexTypePrefixes).ToArray()).ToArray();

        internal sealed class Finding
        {
            internal readonly HashSet<string> Definitions = new HashSet<string>(StringComparer.Ordinal);
            internal readonly HashSet<string> References = new HashSet<string>(StringComparer.Ordinal);
            internal readonly HashSet<string> Strings = new HashSet<string>(StringComparer.Ordinal);
            internal readonly Dictionary<string, string> Locations = new Dictionary<string, string>(StringComparer.Ordinal);
        }

        internal sealed class Result
        {
            internal int DexFiles;
            internal readonly Dictionary<string, Finding> Findings = Packages.ToDictionary(p => p[0], p => new Finding());
            internal bool HasProhibitedTypes => Findings.Values.Any(f => f.Definitions.Count != 0 || f.References.Count != 0);

            internal string Summary()
            {
                var text = new StringBuilder("DEX checked: " + DexFiles + "\n");
                foreach (var pair in Findings)
                {
                    var finding = pair.Value;
                    text.AppendLine(pair.Key + ": DEF=" + finding.Definitions.Count +
                                    ", REF=" + finding.References.Count + ", STR=" + finding.Strings.Count);
                    foreach (var sample in finding.Definitions.Take(5)) text.AppendLine("  DEF " + sample + " (" + finding.Locations[sample] + ")");
                    foreach (var sample in finding.References.Take(5)) text.AppendLine("  REF " + sample + " (" + finding.Locations[sample] + ")");
                }
                text.Append("STR are name/reflection strings only; they do not establish SDK presence.");
                return text.ToString();
            }
        }

        private sealed class ScanBudget
        {
            internal long Bytes;
            internal int Entries;
        }

        internal static Result Scan(string artifactPath)
        {
            var result = new Result();
            using (var stream = File.OpenRead(artifactPath))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
                ScanArchive(archive, Path.GetFileName(artifactPath), result, new ScanBudget(), 0);
            if (result.DexFiles == 0)
                throw new InvalidDataException("No DEX found; the Android artifact could not be verified.");
            return result;
        }

        private static void ScanArchive(ZipArchive archive, string source, Result result, ScanBudget budget, int depth)
        {
            foreach (var entry in archive.Entries)
            {
                if (++budget.Entries > 100000) throw new InvalidDataException("Archive entry limit exceeded.");
                if (entry.Length == 0 && entry.FullName.EndsWith("/", StringComparison.Ordinal)) continue;
                byte[] magic = new byte[8];
                int read = 0;
                using (var stream = entry.Open())
                {
                    while (read < magic.Length)
                    {
                        int count = stream.Read(magic, read, magic.Length - read);
                        if (count == 0) break;
                        read += count;
                    }
                }

                bool dex = read >= 4 && magic[0] == 'd' && magic[1] == 'e' && magic[2] == 'x' && magic[3] == '\n';
                bool zip = read >= 4 && magic[0] == 'P' && magic[1] == 'K' && magic[2] == 3 && magic[3] == 4;
                string location = source + "!" + entry.FullName;
                if (!dex && entry.FullName.EndsWith(".dex", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Unrecognized DEX: " + location);
                if (!dex && !zip) continue;
                // Bound allocations for malformed or unexpectedly huge nested archives. Never silently skip DEX.
                if (entry.Length > 256L * 1024 * 1024 || budget.Bytes + entry.Length > 1024L * 1024 * 1024)
                    throw new InvalidDataException("DEX/archive scan size limit exceeded: " + location);
                using (var stream = entry.Open())
                using (var bytes = new MemoryStream())
                {
                    var buffer = new byte[81920];
                    int count;
                    while ((count = stream.Read(buffer, 0, buffer.Length)) != 0)
                    {
                        if (bytes.Length + count > 256L * 1024 * 1024 ||
                            (budget.Bytes += count) > 1024L * 1024 * 1024)
                            throw new InvalidDataException("Actual archive scan size limit exceeded: " + location);
                        bytes.Write(buffer, 0, count);
                    }
                    if (bytes.Length != entry.Length) throw new InvalidDataException("Archive entry size mismatch: " + location);
                    if (dex)
                    {
                        try { ScanDex(bytes.ToArray(), location, result); }
                        catch (Exception ex) when (ex is InvalidDataException || ex is OverflowException || ex is ArgumentException)
                        { throw new InvalidDataException("Cannot verify " + location + ": " + ex.Message, ex); }
                    }
                    else
                    {
                        if (depth >= 4) throw new InvalidDataException("Nested archive depth limit exceeded: " + location);
                        bytes.Position = 0;
                        using (var nested = new ZipArchive(bytes, ZipArchiveMode.Read, true))
                            ScanArchive(nested, location, result, budget, depth + 1);
                    }
                }
            }
        }

        private static void ScanDex(byte[] data, string source, Result result)
        {
            var packages = Packages;
            Require(data, 0, 0x70);
            string version = Encoding.ASCII.GetString(data, 4, 3);
            if (data[7] != 0 || !new[] { "035", "036", "037", "038", "039", "040" }.Contains(version))
                throw new InvalidDataException("Unsupported DEX version: " + version);
            if (ReadUInt(data, 0x20) != data.Length || ReadUInt(data, 0x24) != 0x70 || ReadUInt(data, 0x28) != 0x12345678)
                throw new InvalidDataException("Invalid DEX size, header or byte order.");
            int stringsCount = ReadInt(data, 0x38), stringsOffset = ReadInt(data, 0x3c);
            int typesCount = ReadInt(data, 0x40), typesOffset = ReadInt(data, 0x44);
            int defsCount = ReadInt(data, 0x60), defsOffset = ReadInt(data, 0x64);
            Require(data, stringsOffset, checked(stringsCount * 4));
            Require(data, typesOffset, checked(typesCount * 4));
            Require(data, defsOffset, checked(defsCount * 32));
            var strings = new string[stringsCount];
            var decoded = new Dictionary<int, string>();
            long decodedBytes = 0;
            for (int i = 0; i < stringsCount; i++)
            {
                int offset = ReadInt(data, stringsOffset + i * 4);
                if (!decoded.TryGetValue(offset, out string value))
                {
                    value = ReadString(data, offset);
                    if ((decodedBytes += value.Length * 2L) > 256L * 1024 * 1024)
                        throw new InvalidDataException("DEX decoded string size limit exceeded.");
                    decoded.Add(offset, value);
                }
                strings[i] = value;
            }
            var types = new string[typesCount];
            for (int i = 0; i < typesCount; i++)
            {
                int index = ReadInt(data, typesOffset + i * 4);
                if (index >= stringsCount) throw new InvalidDataException("Invalid DEX string index.");
                types[i] = strings[index];
            }
            var definitions = new HashSet<int>();
            for (int i = 0; i < defsCount; i++)
            {
                int index = ReadInt(data, defsOffset + i * 32);
                if (index >= typesCount) throw new InvalidDataException("Invalid DEX class index.");
                definitions.Add(index);
            }
            for (int i = 0; i < typesCount; i++)
            {
                string descriptor = types[i].TrimStart('[');
                if (!descriptor.StartsWith("L", StringComparison.Ordinal)) continue;
                foreach (var package in packages)
                {
                    if (!package.Skip(1).Any(prefix => descriptor.StartsWith("L" + prefix, StringComparison.Ordinal))) continue;
                    var finding = result.Findings[package[0]];
                    (definitions.Contains(i) ? finding.Definitions : finding.References).Add(types[i]);
                    if (!finding.Locations.ContainsKey(types[i])) finding.Locations.Add(types[i], source);
                }
            }
            // Names in catalogues are diagnostic only. The type table above determines failure.
            var typeSet = new HashSet<string>(types, StringComparer.Ordinal);
            foreach (string value in strings)
            {
                if (value.Length > 200 || typeSet.Contains(value)) continue;
                foreach (var package in packages)
                {
                    bool match = package.Skip(1).Any(prefix =>
                        value.IndexOf(prefix, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        value.IndexOf(prefix.Replace('/', '.').TrimEnd('.'), StringComparison.OrdinalIgnoreCase) >= 0);
                    if (!match && package[0] != "Branch" && package[0] != "Amplitude")
                        match = package[0].Split(new[] { " / " }, StringSplitOptions.None).Any(name =>
                            value.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (match) result.Findings[package[0]].Strings.Add(value);
                }
            }
            result.DexFiles++;
        }

        private static string ReadString(byte[] data, int offset)
        {
            // Skip ULEB128 UTF-16 length. Package descriptors are ASCII; replacement decoding
            // for non-ASCII MUTF-8 strings cannot change these package prefixes.
            bool terminated = false;
            for (int i = 0; i < 5; i++)
            {
                Require(data, offset, 1);
                byte value = data[offset++];
                if ((value & 0x80) == 0) { terminated = true; break; }
            }
            if (!terminated) throw new InvalidDataException("Invalid DEX string length.");
            int end = offset;
            while (end < data.Length && data[end] != 0) end++;
            if (end == data.Length) throw new InvalidDataException("Unterminated DEX string.");
            if (end - offset > 1024 * 1024) throw new InvalidDataException("DEX string size limit exceeded.");
            return Encoding.UTF8.GetString(data, offset, end - offset);
        }

        private static uint ReadUInt(byte[] data, int offset)
        {
            Require(data, offset, 4);
            return (uint)(data[offset] | data[offset + 1] << 8 | data[offset + 2] << 16 | data[offset + 3] << 24);
        }

        private static int ReadInt(byte[] data, int offset) => checked((int)ReadUInt(data, offset));

        private static void Require(byte[] data, int offset, int length)
        {
            if (offset < 0 || length < 0 || offset > data.Length - length)
                throw new InvalidDataException("DEX table is outside the file.");
        }
    }
}
