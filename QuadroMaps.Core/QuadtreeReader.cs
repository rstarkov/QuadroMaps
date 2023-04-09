using System.Collections.Concurrent;

namespace QuadroMaps.Core;

public class QuadtreeReaderContext : IDisposable
{
    public BinaryReader Reader;
    public BinaryReader StringsReader;
    public int LatBits, LonBits;

    public void Dispose()
    {
        StringsReader?.Dispose();
    }
}

public abstract class QuadtreeReader<T>
{
    private string _filename;
    private int _depthLimit;

    public QuadtreeReader(string filename, int depthLimit)
    {
        _filename = filename;
        _depthLimit = depthLimit;
    }

    protected virtual QuadtreeReaderContext StartRead() { return new QuadtreeReaderContext(); }
    protected virtual void ReadHeader(QuadtreeReaderContext c) { }
    protected abstract T ReadItem(QuadtreeReaderContext c);

    public IEnumerable<T> ReadArea(LatLonRect area)
    {
        using var stream = File.Open(_filename, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var br = new BinaryReader(stream);
        using var ctx = StartRead();
        ctx.Reader = br;

        ReadHeader(ctx);

        var (latRangeBits, latRangeMask) = area.SameBitsLat;
        var (lonRangeBits, lonRangeMask) = area.SameBitsLon;

        var state = new Stack<(int depth, int latBits, int lonBits, long pos)>();
        state.Push((0, 0, 0, br.BaseStream.Position));
        while (state.Count > 0)
        {
            var (depth, latBits, lonBits, pos) = state.Pop();

            int lat = latBits << (32 - depth); // at depth=0 the left side of the shift is guaranteed to be zero
            int lon = lonBits << (32 - depth); //   so the overall result is correct even though x<<32 is a no-op.
            if (depth > 0)
            {
                var mask = ~((1 << (32 - depth)) - 1);
                if ((latRangeBits & mask) != (lat & latRangeMask) || (lonRangeBits & mask) != (lon & lonRangeMask))
                    continue;
            }

            br.BaseStream.Position = pos;
            var next01 = depth == _depthLimit ? uint.MaxValue : br.ReadUInt32();
            if (next01 == uint.MaxValue)
            {
                int count = br.Read7BitEncodedInt();
                ctx.LatBits = lat;
                ctx.LonBits = lon;
                for (int i = 0; i < count; i++)
                    yield return ReadItem(ctx);
                continue;
            }
            var next10 = br.ReadUInt32();
            var next11 = br.ReadUInt32();
            depth++;
            latBits <<= 1;
            lonBits <<= 1;
            state.Push((depth, latBits, lonBits, br.BaseStream.Position));
            state.Push((depth, latBits, lonBits | 1, next01));
            state.Push((depth, latBits | 1, lonBits, next10));
            state.Push((depth, latBits | 1, lonBits | 1, next11));
        }
    }
}

public class NodeTagsQuadtreeReader : QuadtreeReader<NodeTagsQuadtreeReader.Entry>
{
    public class Entry
    {
        public LatLon Point;
        public string TagValue;
    }

    private string _stringsFilename;
    private string _tagValue;
    private ConcurrentDictionary<long, string> _strings = new ConcurrentDictionary<long, string>();

    public NodeTagsQuadtreeReader(string qtrFilename, string stringsFilename, string tagValue) : base(qtrFilename, 16)
    {
        _stringsFilename = stringsFilename;
        _tagValue = tagValue;
    }

    protected override void ReadHeader(QuadtreeReaderContext c)
    {
        var header = c.Reader.ReadBytes(4);
    }

    protected override Entry ReadItem(QuadtreeReaderContext c)
    {
        var result = new Entry();
        var ilat = c.LatBits | c.Reader.ReadUInt16();
        var ilon = c.LonBits | c.Reader.ReadUInt16();
        result.Point = LatLon.FromInt(ilat, ilon);
        if (_tagValue != null)
            result.TagValue = _tagValue;
        else if (_stringsFilename == null)
            result.TagValue = c.Reader.ReadString();
        else
        {
            var stringPos = c.Reader.Read7BitEncodedInt64();
            if (_strings.TryGetValue(stringPos, out var str))
                result.TagValue = str;
            else
            {
                if (c.StringsReader == null)
                    c.StringsReader = new BinaryReader(File.Open(_stringsFilename, FileMode.Open, FileAccess.Read, FileShare.Read));
                c.StringsReader.BaseStream.Position = stringPos;
                result.TagValue = c.StringsReader.ReadString();
                _strings[stringPos] = result.TagValue;
            }
        }
        return result;
    }
}

//public class WayTagsQuadtreeReader : QuadtreeReader<NodeTagsQuadtreeReader.Entry>
//{
//    public class Entry
//    {
//        public LatLon Point;
//        public string TagValue;
//    }

//    public WayTagsQuadtreeReader(string filename) : base(filename, 16)
//    {
//    }

//    protected override Entry ReadItem(BinaryReader reader, int latBits, int lonBits)
//    {
//        var result = new Entry();
//        result.Point.ILat = latBits | reader.ReadUInt16();
//        result.Point.ILon = lonBits | reader.ReadUInt16();
//        return result;
//    }
//}
