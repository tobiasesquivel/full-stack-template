using System.Buffers.Binary;
using System.Buffers.Text;

namespace TemplateApp.Features.Tasks.ListTasks;

internal sealed record TaskCursor(DateTimeOffset CreatedAt, Guid Id)
{
    private const byte Version = 1;
    private const int Length = 1 + sizeof(long) + 16;

    public static string Encode(TaskResponse last)
    {
        Span<byte> bytes = stackalloc byte[Length];
        bytes[0] = Version;
        BinaryPrimitives.WriteInt64LittleEndian(bytes[1..], last.CreatedAt.UtcTicks);
        last.Id.TryWriteBytes(bytes[(1 + sizeof(long))..]);
        return Base64Url.EncodeToString(bytes);
    }

    public static bool TryDecode(string text, out TaskCursor cursor)
    {
        cursor = new TaskCursor(default, Guid.Empty);
        if (!Base64Url.IsValid(text, out int decodedLength) || decodedLength != Length)
            return false;

        Span<byte> bytes = stackalloc byte[Length];
        Base64Url.DecodeFromChars(text, bytes);
        if (bytes[0] != Version)
            return false;

        long ticks = BinaryPrimitives.ReadInt64LittleEndian(bytes[1..]);
        if (ticks < DateTimeOffset.MinValue.UtcTicks || ticks > DateTimeOffset.MaxValue.UtcTicks)
            return false;

        cursor = new TaskCursor(new DateTimeOffset(ticks, TimeSpan.Zero), new Guid(bytes[(1 + sizeof(long))..Length]));
        return true;
    }
}
