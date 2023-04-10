using System.Security.Cryptography;
using OsmSharp;
using QuadroMaps.Core;
using RT.Util;
using RT.Util.Collections;
using RT.Util.ExtensionMethods;

namespace QuadroMaps.Pbf;

public class PbfConverter
{
    private string _pbfFilename, _dbPath;
    private Dictionary<string, BinaryWriter2> _filestreams = new();

    private BinaryWriter2 filestream(string path, string name)
    {
        var nameNoExt = Path.GetFileNameWithoutExtension(name);
        if (nameNoExt.Any(c => char.IsUpper(c))) // hash suffix is necessary due to case insensitivity of the Windows filesystems
            name = PathUtil.AppendBeforeExtension(name, "." + MD5.Create().ComputeHash(nameNoExt.ToUtf8()).ToHex()[..6].ToLower());
        var fullpath = Path.Combine(_dbPath.Concat(path.Split(':').Select(s => s.EscapeFilename())).Concat(name.EscapeFilename()).ToArray());
        if (_filestreams.TryGetValue(fullpath, out var result))
            return result;
        Directory.CreateDirectory(Path.GetDirectoryName(fullpath));
        return _filestreams[fullpath] = new BinaryWriter2(File.Open(fullpath, FileMode.Create, FileAccess.Write, FileShare.Read)); // this can throw File Already Opened - it's an indication of a naming conflict (file case? trailing dots?)
    }

    private BinaryWriter2 createfile(string path, string name, string headerID, string ver, int count)
    {
        if (headerID.Length != 4 || ver.Length != 1)
            throw new Exception();
        var writer = filestream(path, name);
        writer.Write($"{headerID.ToUpper()}:{ver}:{(count == 0 ? "" : count.ClipMax(9999999).ToString()).PadLeft(7)}:".ToCharArray()); // length = 15
        return writer;
    }

    private BinaryWriter2 createfile(string path, string name, string headerID, string ver, Func<int> getCount)
    {
        var writer = createfile(path, name, headerID, ver, 0);
        writer.BeforeDispose = (self) =>
        {
            self.Position = 7;
            self.Write($"{getCount().ClipMax(9999999),7}".ToCharArray());
        };
        return writer;
    }

    public PbfConverter(string pbfFilename, string dbPath)
    {
        _pbfFilename = pbfFilename;
        _dbPath = dbPath;
    }

    public void Convert()
    {
        var nodes = new Dictionary<long, ulong>();
        var wayRenumber = new Dictionary<long, uint>();
        var relRenumber = new Dictionary<long, uint>();
        var wayTags = new AutoDictionary<string, string, List<uint>>((_, __) => new List<uint>());
        var nodeTags = new AutoDictionary<string, string, List<ulong>>((_, __) => new List<ulong>());
        var relTags = new AutoDictionary<string, string, List<uint>>((_, __) => new List<uint>());
        var wayAreas = new AutoDictionary<uint, LatLonRect>(_ => new LatLonRect());
        var relAreas = new AutoDictionary<uint, LatLonRect>(_ => new LatLonRect());
        var relStrings = new StringsCacher(this, "", "rels.strings");
        long prevWayId = 0, prevWayIdPos = 0;
        long prevRelId = 0, prevRelIdPos = 0;
        var bwWays = createfile("", "ways.dat", "WAYS", "1", () => wayRenumber.Count);
        var bwWaysOffsets = createfile("", "ways.offsets", "OFFS", "1", () => wayRenumber.Count);
        var bwWaysOsmId = createfile("", "osm_ids.ways.dat", "OIDS", "1", () => wayRenumber.Count);
        var bwRels = createfile("", "rels.dat", "RELS", "1", () => relRenumber.Count);
        var bwRelsOffsets = createfile("", "rels.offsets", "OFFS", "1", () => relRenumber.Count);
        var bwRelsOsmId = createfile("", "osm_ids.rels.dat", "OIDS", "1", () => relRenumber.Count);
        foreach (var el in PbfUtil.ReadPbf(_pbfFilename, relsLast: true))
        {
            if (el is Node node)
            {
                var n = LatLon.FromDeg(node.Latitude.Value, node.Longitude.Value).Packed;
                nodes.Add(node.Id.Value, n);
                foreach (var tag in node.Tags)
                    nodeTags[tag.Key][tag.Value].Add(n);
            }
            else if (el is Way way)
            {
                var wayId = (uint)(wayRenumber.Count + 1);
                wayRenumber.Add(way.Id.Value, wayId);
                bwWaysOffsets.Write7BitEncodedInt64(bwWays.Position - prevWayIdPos); // to be loaded into RAM during usage
                prevWayIdPos = bwWays.Position;
                bwWays.Write7BitEncodedInt(way.Nodes.Length);
                int prevLat = 0, prevLon = 0;
                for (int i = 0; i < way.Nodes.Length; i++)
                {
                    var n = nodes[way.Nodes[i]];
                    var ll = LatLon.FromPacked(n);
                    if (i == 0)
                        bwWays.Write(n);
                    else
                    {
                        bwWays.Write7BitEncodedSignedInt(ll.ILat - prevLat);
                        bwWays.Write7BitEncodedSignedInt(ll.ILon - prevLon);
                    }
                    prevLat = ll.ILat;
                    prevLon = ll.ILon;
                    wayAreas[wayId].AddLatLon(ll);
                }
                foreach (var tag in way.Tags)
                    wayTags[tag.Key][tag.Value].Add(wayId);
                bwWaysOsmId.Write7BitEncodedInt64(way.Id.Value - prevWayId);
                prevWayId = way.Id.Value;
            }
            else if (el is Relation rel)
            {
                var relId = (uint)(relRenumber.Count + 1);
                relRenumber.Add(rel.Id.Value, relId);
                bwRelsOffsets.Write7BitEncodedInt64(bwRels.Position - prevRelIdPos); // to be loaded into RAM during usage
                prevRelIdPos = bwRels.Position;
                bwRels.Write7BitEncodedInt(rel.Members.Length);
                foreach (var m in rel.Members)
                {
                    if (m.Type == OsmGeoType.Node)
                    {
                        if (nodes.ContainsKey(m.Id)) // area-limited pbf dumps include relations with nodes that have been trimmed as out of area
                        {
                            bwRels.Write('N');
                            bwRels.Write7BitEncodedInt64(relStrings[m.Role]);
                            bwRels.Write(nodes[m.Id]);
                            relAreas[relId].AddLatLon(LatLon.FromPacked(nodes[m.Id]));
                        }
                    }
                    else if (m.Type == OsmGeoType.Way)
                    {
                        if (wayRenumber.ContainsKey(m.Id)) // area-limited pbf dumps include relations with ways that have been trimmed as out of area
                        {
                            bwRels.Write('W');
                            bwRels.Write7BitEncodedInt64(relStrings[m.Role]);
                            bwRels.Write7BitEncodedInt((int)wayRenumber[m.Id]);
                            relAreas[relId].AddArea(wayAreas[wayRenumber[m.Id]]);
                        }
                    }
                    else if (m.Type == OsmGeoType.Relation)
                    {
                        if (relRenumber.ContainsKey(m.Id)) // area-limited pbf dumps include relations with sub-relations that have been trimmed as out of area
                        {
                            bwRels.Write('R');
                            bwRels.Write7BitEncodedInt64(relStrings[m.Role]);
                            bwRels.Write7BitEncodedInt((int)relRenumber[m.Id]);
                            relAreas[relId].AddArea(relAreas[relRenumber[m.Id]]);
                        }
                    }
                    else
                        throw new Exception();
                }
                foreach (var tag in rel.Tags)
                    relTags[tag.Key][tag.Value].Add(relId);
                bwRelsOsmId.Write7BitEncodedInt64(rel.Id.Value - prevRelId);
                prevRelId = rel.Id.Value;
            }
        }
        static void saveQuadtree<T>(BinaryWriter2 bw, List<T> items, int depthLimit, int itemsLimit, Func<T, int, int, int, bool> filter, Action<T, BinaryWriter2> writer)
            => new QuadtreeWriter<T>(bw, depthLimit, itemsLimit, filter, writer).WriteQuadtree(items);
        void saveTags<T>(AutoDictionary<string, string, List<T>> tags, string kind, int depthLimit, int itemsLimit, Func<T, int, int, int, bool> filter, Action<T, BinaryWriter2> writer)
        {
            var headerID = kind == "node" ? "NTAG" : kind == "way" ? "WTAG" : kind == "rel" ? "RTAG" : throw new Exception();
            foreach (var tagKey in tags.Keys)
            {
                var otherValues = new List<string>();
                foreach (var tagVal in tags[tagKey].Keys)
                {
                    if (tags[tagKey][tagVal].Count <= 500)
                        otherValues.Add(tagVal);
                    else
                    {
                        var bw = createfile(tagKey, $"{kind}.tag.{tagKey}={tagVal}.qtr", headerID, "1", tags[tagKey][tagVal].Count);
                        saveQuadtree(bw, tags[tagKey][tagVal], depthLimit, itemsLimit, filter, writer);
                    }
                }
                var remainingTags = otherValues.SelectMany(tagValue => tags[tagKey][tagValue].Select(n => (tagValue, n))).ToList();
                var bw2 = createfile(tagKey, $"{kind}.tag.{tagKey}.qtr", headerID, "1", remainingTags.Count);
                var strings = remainingTags.Count < 500 ? null : new StringsCacher(this, tagKey, $"{kind}.tag.{tagKey}.strings");
                saveQuadtree(bw2, remainingTags, depthLimit, itemsLimit,
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
        // this is pointless as long as multiple nodes can be located at the same coordinates
        //saveQuadtree(filestream("", "node.osm_ids.qtr"), nodes.ToList(), 16, 0,
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
            (t, lat, lon, mask) => { var (ilat, ilon) = LatLon.Unpack(t); return (ilat & mask) == lat && (ilon & mask) == lon; },
            (t, bw) =>
            {
                var (ilat, ilon) = LatLon.Unpack(t);
                bw.Write((ushort)ilat);
                bw.Write((ushort)ilon);
            });

        foreach (var fs in _filestreams.Values)
            fs.Dispose();
    }

    private class StringsCacher
    {
        private Dictionary<string, long> _map = new Dictionary<string, long>();
        private PbfConverter _converter;
        private BinaryWriter2 _bwStrings;
        private string _path, _name;

        public StringsCacher(PbfConverter converter, string path, string name)
        {
            _converter = converter;
            _path = path;
            _name = name;
        }

        public long this[string value]
        {
            get
            {
                if (_map.TryGetValue(value, out long result))
                    return result;
                if (_bwStrings == null)
                    _bwStrings = _converter.createfile(_path, _name, "STRN", "1", () => _map.Count);
                result = _bwStrings.Position;
                _bwStrings.Write(value);
                _map.Add(value, result);
                return result;
            }
        }
    }
}
