using System.Security.Cryptography;
using OsmSharp;
using RT.Util;
using RT.Util.Collections;
using RT.Util.ExtensionMethods;

namespace Painter;

public class MapData
{
    public static void Generate(string pbfFilename, string dbPath)
    {
        // todo:
        // - ways.dat should be geospatially sorted despite not being geospatially indexed
        // - bsp leaf items can be sorted and diff-encoded. And/or maybe lz4'd?

        Stream openfile(string name) { Directory.CreateDirectory(Path.GetDirectoryName(name)); return File.Open(name, FileMode.Create, FileAccess.Write, FileShare.Read); }
        string hash(string val) => MD5.Create().ComputeHash(val.ToUtf8()).ToHex()[..6].ToLower();
        var filestreams = new AutoDictionary<string, BinaryWriter>(fname => new BinaryWriter(openfile(fname)));
        BinaryWriter filestream(string path, string name) => filestreams[Path.Combine(dbPath.Concat(path.Split(':').Select(s => s.FilenameCharactersEscape())).Concat(PathUtil.AppendBeforeExtension(name, "." + hash(Path.GetFileNameWithoutExtension(name))).FilenameCharactersEscape()).ToArray())];
        var nodes = new Dictionary<long, ulong>();
        var wayRenumber = new Dictionary<long, uint>();
        var relRenumber = new Dictionary<long, uint>();
        var wayTags = new AutoDictionary<string, string, List<uint>>((_, __) => new List<uint>());
        var nodeTags = new AutoDictionary<string, string, List<ulong>>((_, __) => new List<ulong>());
        var relTags = new AutoDictionary<string, string, List<uint>>((_, __) => new List<uint>());
        var wayAreas = new AutoDictionary<uint, RectArea>(_ => new RectArea());
        var relAreas = new AutoDictionary<uint, RectArea>(_ => new RectArea());
        var relStrings = new StringsCacher(() => filestream("", "rels.strings"));
        long prevWayId = 0;
        long prevRelId = 0;
        foreach (var el in Utils.ReadPbf(pbfFilename, relsLast: true))
        {
            if (el is Node node)
            {
                var n = latlon2node(node.Latitude.Value, node.Longitude.Value);
                nodes.Add(node.Id.Value, n);
                foreach (var tag in node.Tags)
                    nodeTags[tag.Key][tag.Value].Add(n);
            }
            else if (el is Way way)
            {
                var wayId = (uint)(wayRenumber.Count + 1);
                wayRenumber.Add(way.Id.Value, wayId);
                filestream("", "ways.id.dat").Write7BitEncodedInt64(filestream("", "ways.dat").BaseStream.Position); // to be loaded into RAM during usage
                filestream("", "ways.dat").Write7BitEncodedInt(way.Nodes.Length);
                foreach (var n in way.Nodes)
                {
                    filestream("", "ways.dat").Write(nodes[n]);
                    wayAreas[wayId].AddNode(nodes[n]);
                }
                foreach (var tag in way.Tags)
                    wayTags[tag.Key][tag.Value].Add(wayId);
                filestream("", "ways.osm_id.dat").Write7BitEncodedInt64(way.Id.Value - prevWayId);
                prevWayId = way.Id.Value;
            }
            else if (el is Relation rel)
            {
                var relId = (uint)(relRenumber.Count + 1);
                relRenumber.Add(rel.Id.Value, relId);
                var bw = filestream("", "rels.dat");
                filestream("", "rels.id.dat").Write7BitEncodedInt64(bw.BaseStream.Position); // to be loaded into RAM during usage
                bw.Write7BitEncodedInt(rel.Members.Length);
                foreach (var m in rel.Members)
                {
                    if (m.Type == OsmGeoType.Node)
                    {
                        if (nodes.ContainsKey(m.Id)) // area-limited pbf dumps include relations with nodes that have been trimmed as out of area
                        {
                            bw.Write('N');
                            bw.Write7BitEncodedInt64(relStrings[m.Role]);
                            bw.Write(nodes[m.Id]);
                            relAreas[relId].AddNode(nodes[m.Id]);
                        }
                    }
                    else if (m.Type == OsmGeoType.Way)
                    {
                        if (wayRenumber.ContainsKey(m.Id)) // area-limited pbf dumps include relations with ways that have been trimmed as out of area
                        {
                            bw.Write('W');
                            bw.Write7BitEncodedInt64(relStrings[m.Role]);
                            bw.Write7BitEncodedInt((int)wayRenumber[m.Id]);
                            relAreas[relId].AddArea(wayAreas[wayRenumber[m.Id]]);
                        }
                    }
                    else if (m.Type == OsmGeoType.Relation)
                    {
                        if (relRenumber.ContainsKey(m.Id)) // area-limited pbf dumps include relations with sub-relations that have been trimmed as out of area
                        {
                            bw.Write('R');
                            bw.Write7BitEncodedInt64(relStrings[m.Role]);
                            bw.Write7BitEncodedInt((int)relRenumber[m.Id]);
                            relAreas[relId].AddArea(relAreas[relRenumber[m.Id]]);
                        }
                    }
                    else
                        throw new Exception();
                }
                foreach (var tag in rel.Tags)
                    relTags[tag.Key][tag.Value].Add(relId);
                filestream("", "rels.osm_id.dat").Write7BitEncodedInt64(rel.Id.Value - prevRelId);
                prevRelId = rel.Id.Value;
            }
        }
        void saveBsp<T>(BinaryWriter bw, List<T> items, int depthLimit, int itemsLimit, Func<T, int, int, int, bool> filter, Action<T, BinaryWriter> writer)
            => new BspWriter<T>(bw, depthLimit, itemsLimit, filter, writer).SaveBsp(items);
        void saveTags<T>(AutoDictionary<string, string, List<T>> tags, string kind, int depthLimit, int itemsLimit, Func<T, int, int, int, bool> filter, Action<T, BinaryWriter> writer)
        {
            foreach (var tagKey in tags.Keys)
            {
                var otherValues = new List<string>();
                foreach (var tagVal in tags[tagKey].Keys)
                {
                    if (tags[tagKey][tagVal].Count <= 500)
                        otherValues.Add(tagVal);
                    else
                    {
                        var bw = filestream(tagKey, $"{kind}.tag.{tagKey}={tagVal}.bsp");
                        saveBsp(bw, tags[tagKey][tagVal], depthLimit, itemsLimit, filter, writer);
                    }
                }
                var bw2 = filestream(tagKey, $"{kind}.tag.{tagKey}.bsp");
                var remainingTags = otherValues.SelectMany(tagValue => tags[tagKey][tagValue].Select(n => (tagValue, n))).ToList();
                var strings = remainingTags.Count < 500 ? null : new StringsCacher(() => filestream(tagKey, $"{kind}.tag.{tagKey}.strings"));
                saveBsp(bw2, remainingTags, depthLimit, itemsLimit,
                    (t, lat, lon, mask) => filter(t.n, lat, lon, mask),
                    (t, bw) =>
                    {
                        writer(t.n, bw);
                        if (strings == null)
                            bw.Write(t.tagValue);
                        else
                            bw.Write7BitEncodedInt64(strings[t.tagValue]);
                    });
            }
        }
        //saveBsp(filestream("", "node.osm_ids.bsp"), nodes.ToList(), 16, 0,
        //    (t, lat, lon, mask) => { var (ilat, ilon) = node2ilatlon(t.Value); return (ilat & mask) == lat && (ilon & mask) == lon; },
        //    (t, bw) =>
        //    {
        //        var (ilat, ilon) = node2ilatlon(t.Value);
        //        bw.Write((ushort)ilat);
        //        bw.Write((ushort)ilon);
        //        bw.Write7BitEncodedInt64(t.Key);
        //    });
        saveTags(wayTags, "way", 16, 300,
            (t, lat, lon, mask) => wayAreas[t].Overlaps(lat, lon, mask),
            (t, bw) => bw.Write(t));
        saveTags(relTags, "rel", 14, 500,
            (t, lat, lon, mask) => relAreas[t].Overlaps(lat, lon, mask),
            (t, bw) => bw.Write(t));
        saveTags(nodeTags, "node", 16, 0, // 0 because we must reach depth 16 no matter what, because the writer assumes we can reconstruct the top bits of the coordinates
            (t, lat, lon, mask) => { var (ilat, ilon) = node2ilatlon(t); return (ilat & mask) == lat && (ilon & mask) == lon; },
            (t, bw) =>
            {
                var (ilat, ilon) = node2ilatlon(t);
                bw.Write((ushort)ilat);
                bw.Write((ushort)ilon);
            });

        foreach (var fs in filestreams.Values)
            fs.Dispose();
    }

    public static ulong latlon2node(double lat, double lon)
    {
        var ilat = checked((int)Math.Round(lat * 10_000_000));
        var ilon = checked((int)Math.Round(lon * 10_000_000));
        return ilatlon2node(ilat, ilon);
    }

    public static ulong ilatlon2node(int ilat, int ilon)
    {
        return ((ulong)(uint)ilat << 32) | (uint)ilon;
    }

    public static (int lat, int lon) node2ilatlon(ulong node)
    {
        int lat = (int)(uint)(node >> 32);
        int lon = (int)(uint)node;
        return (lat, lon);
    }

    static (uint, double, double) latlon2bsp(double lat, double lon)
    {
        double latMin = -90;
        double latMax = 90;
        double lonMin = -180;
        double lonMax = 180;
        uint result = 0;
        for (int i = 0; i < 16; i++)
        {
            double c = (lonMax + lonMin) / 2;
            if (lon < c)
                lonMax = c;
            else
            {
                lonMin = c;
                result |= 1;
            }
            result <<= 1;
            c = (latMax + latMin) / 2;
            if (lat < c)
                latMax = c;
            else
            {
                latMin = c;
                result |= 1;
            }
            result <<= 1;
        }
        return (result, latMin, lonMin);
    }
}

public class RectArea
{
    public int LatMin = int.MaxValue, LatMax, LonMin, LonMax;

    public void AddNode(ulong node)
    {
        (int lat, int lon) = MapData.node2ilatlon(node);
        AddLatLon(lat, lon);
    }

    public void AddLatLon(int ilat, int ilon)
    {
        if (LatMin == int.MaxValue)
        {
            LatMin = LatMax = ilat;
            LonMin = LonMax = ilon;
        }
        else
        {
            LatMin = Math.Min(LatMin, ilat);
            LatMax = Math.Max(LatMax, ilat);
            LonMin = Math.Min(LonMin, ilon);
            LonMax = Math.Max(LonMax, ilon);
        }
    }

    public void AddArea(RectArea area)
    {
        AddLatLon(area.LatMin, area.LonMin);
        AddLatLon(area.LatMax, area.LonMax);
    }

    public bool Overlaps(int lat, int lon, int mask)
    {
        var latMX = lat + ~mask;
        var lonMX = lon + ~mask;
        return (lon <= LonMax) && (lonMX >= LonMin) && (lat <= LatMax) && (latMX >= LatMin);
    }
}

public class StringsCacher
{
    private Dictionary<string, long> _map = new Dictionary<string, long>();
    private Func<BinaryWriter> _getWriter;
    private BinaryWriter _bwStrings;

    public StringsCacher(Func<BinaryWriter> getWriter)
    {
        _getWriter = getWriter;
    }

    public long this[string value]
    {
        get
        {
            if (_map.TryGetValue(value, out long result))
                return result;
            if (_bwStrings == null)
                _bwStrings = _getWriter();
            result = _bwStrings.BaseStream.Position;
            _bwStrings.Write(value);
            _map.Add(value, result);
            return result;
        }
    }
}

public class BspWriter<T>
{
    public BspWriter(BinaryWriter bw, int depthLimit, int itemsCountLimit, Func<T, int, int, int, bool> filter, Action<T, BinaryWriter> write)
    {
        _bw = bw;
        _depthLimit = depthLimit;
        _itemsCountLimit = itemsCountLimit;
        _filter = filter;
        _write = write;
    }

    private BinaryWriter _bw;
    private int _depthLimit;
    private int _itemsCountLimit;
    private Func<T, int, int, int, bool> _filter;
    private Action<T, BinaryWriter> _write;

    public void SaveBsp(List<T> items)
    {
        saveBsp(items, 0, 0, 0);
    }

    private void saveBsp(List<T> items, int depth, int latBits, int lonBits)
    {
        var mask = ~((1 << (32 - depth)) - 1);
        if (depth > 0)
        {
            int lat = latBits << (32 - depth);
            int lon = lonBits << (32 - depth);
            items = items.Where(t => _filter(t, lat, lon, mask)).ToList();
        }
        if (depth == _depthLimit || items.Count == 0 || items.Count < _itemsCountLimit)
        {
            if (depth != _depthLimit)
                _bw.Write(uint.MaxValue); // marker of early exit from depth recursion
            _bw.Write7BitEncodedInt(items.Count);
            foreach (var item in items)
                _write(item, _bw);
            return;
        }

        depth++;
        mask = ~((1 << (32 - depth)) - 1);
        latBits <<= 1;
        lonBits <<= 1;

        var backpatch = _bw.BaseStream.Position;
        _bw.Write(0); // 01
        _bw.Write(0); // 10
        _bw.Write(0); // 11

        saveBsp(items, depth, latBits, lonBits);
        _bw.BaseStream.Position = backpatch;
        _bw.Write(checked((uint)_bw.BaseStream.Length));
        _bw.BaseStream.Position = _bw.BaseStream.Length;
        saveBsp(items, depth, latBits, lonBits | 1);
        _bw.BaseStream.Position = backpatch + 4;
        _bw.Write(checked((uint)_bw.BaseStream.Length));
        _bw.BaseStream.Position = _bw.BaseStream.Length;
        saveBsp(items, depth, latBits | 1, lonBits);
        _bw.BaseStream.Position = backpatch + 8;
        _bw.Write(checked((uint)_bw.BaseStream.Length));
        _bw.BaseStream.Position = _bw.BaseStream.Length;
        saveBsp(items, depth, latBits | 1, lonBits | 1);
    }
}
