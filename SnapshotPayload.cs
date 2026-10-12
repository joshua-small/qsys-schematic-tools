using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using System.Text;

namespace Designer.Modules.SelectionProbe;

internal sealed record SnapshotDatum(float? Number, string? Text, bool IsText)
{
    internal string Display => IsText ? string.IsNullOrEmpty(Text) ? "Empty" : Text : Number?.ToString("G9", CultureInfo.InvariantCulture) ?? "Unavailable";
    // Native saved empty strings use null; current string feedback uses "".
    // Absence of a record is handled separately and is never an empty value.
    internal bool SameValue(SnapshotDatum other) => IsText == other.IsText && (IsText ? (Text ?? "") == (other.Text ?? "") : Number.Equals(other.Number));
}

/// <summary>Bounded, non-executing reader for the observed Designer 10.5 controlset format.</summary>
internal static class SnapshotPayload
{
    internal const int MaximumFileBytes = 16 * 1024 * 1024;
    private const int MaximumExpandedBytes = 32 * 1024 * 1024;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    internal static Dictionary<string, SnapshotDatum> Read(byte[] bytes)
    {
        if (bytes.Length < 35 || bytes.Length > MaximumFileBytes) throw new InvalidDataException("Snapshot file size is unsupported.");
        // The native trailer is preserved and checked for its observed shape.
        // Its checksum algorithm is unresolved; this is structural validation.
        if (bytes.AsSpan(bytes.Length - 32).ToArray().Any(b => !(b is >= (byte)'0' and <= (byte)'9' or >= (byte)'A' and <= (byte)'F')))
            throw new InvalidDataException("Snapshot trailer is invalid.");
        if (bytes[0] != (byte)'q') throw new InvalidDataException("Unsupported snapshot encoding.");
        var output = new List<byte>(); int end = bytes.Length - 32;
        for (int p = 1; p < end;)
        {
            int tag = bytes[p++], count = tag < 128 ? tag : tag & 31;
            if (count == 0 || output.Count + count > MaximumExpandedBytes) throw new InvalidDataException("Invalid snapshot expansion.");
            if (tag < 128)
            {
                if (p + count > end) throw new InvalidDataException("Truncated snapshot literal.");
                for (int i = 0; i < count; i++) output.Add(bytes[p++]);
            }
            else
            {
                if (p >= end) throw new InvalidDataException("Truncated snapshot reference.");
                int distance = ((tag & 96) << 3) | bytes[p++];
                if (distance == 0 || distance > output.Count) throw new InvalidDataException("Invalid snapshot reference.");
                for (int i = 0; i < count; i++) output.Add(output[output.Count - distance]);
            }
        }
        var reader = new Reader(output.ToArray());
        if (reader.Integer() != 0x0bf7fbd9) throw new InvalidDataException("Unsupported snapshot version.");
        int records = reader.Integer();
        if (records < 0 || records > 100000) throw new InvalidDataException("Invalid snapshot control count.");
        var values = new Dictionary<string, SnapshotDatum>(StringComparer.Ordinal);
        for (int i = 0; i < records; i++)
        {
            string key = reader.String() ?? throw new InvalidDataException("Snapshot control ID is empty.");
            int kind = reader.Integer();
            SnapshotDatum datum = kind switch
            {
                7 => new(reader.Single(), null, false),
                4 => new(null, reader.String(), true),
                _ => throw new InvalidDataException("Unsupported snapshot value type " + kind + ".")
            };
            if (!values.TryAdd(key, datum)) throw new InvalidDataException("Duplicate snapshot control ID.");
        }
        if (!reader.AtEnd) throw new InvalidDataException("Unexpected data after snapshot controls.");
        return values;
    }
    private sealed class Reader(byte[] data)
    {
        private int position;
        internal bool AtEnd => position == data.Length;
        private byte Byte() { if (position >= data.Length) throw new InvalidDataException("Truncated snapshot value."); return data[position++]; }
        private void Tag(char expected) { if (Byte() != expected) throw new InvalidDataException("Unexpected snapshot value encoding."); }
        private int Int32() { if (position + 4 > data.Length) throw new InvalidDataException("Truncated snapshot integer."); int result = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(position, 4)); position += 4; return result; }
        internal int Integer() { Tag('i'); return Int32(); }
        internal float Single() { Tag('e'); return BitConverter.Int32BitsToSingle(Int32()); }
        internal string? String()
        {
            byte tag = Byte(); if (tag == 'n') return null; if (tag != 's') throw new InvalidDataException("Unexpected snapshot string encoding.");
            int length = Int32(); if (length < 0 || length > MaximumFileBytes || length > data.Length - position) throw new InvalidDataException("Invalid snapshot string length.");
            string value = Utf8.GetString(data, position, length); position += length; return value;
        }
    }
}
