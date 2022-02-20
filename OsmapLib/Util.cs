namespace OsmapLib;

public sealed class BinaryWriter2 : BinaryWriter
{
    public BinaryWriter2(Stream stream) : base(stream) { }
    public long Position { get { return OutStream.Position; } set { OutStream.Position = value; } }
    public long Length => OutStream.Length;
    public override Stream BaseStream => OutStream; // we don't do buffered writes, so no need to flush the stream like the base implementation getter does
}

public static class ExtensionMethods
{
    public static void Write7BitEncodedSignedInt(this BinaryWriter2 bw, int value)
    {
        // from RT.Util WriteInt32Optim
        while (value < -64 || value > 63)
        {
            bw.Write((byte)(value | 128));
            value >>= 7;
        }
        bw.Write((byte)(value & 127));
    }

    public static void Write7BitEncodedSignedInt64(this BinaryWriter2 bw, long value)
    {
        // from RT.Util WriteInt64Optim
        while (value < -64 || value > 63)
        {
            bw.Write((byte)(value | 128));
            value >>= 7;
        }
        bw.Write((byte)(value & 127));
    }
}
