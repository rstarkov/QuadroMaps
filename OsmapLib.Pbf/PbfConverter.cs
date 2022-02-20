using System.Security.Cryptography;
using OsmSharp;
using RT.Util;
using RT.Util.Collections;
using RT.Util.ExtensionMethods;

namespace OsmapLib.Generator;

public class PbfConverter
{
    public void Convert(string pbfFilename, string dbPath)
    {
        // todo:
        // - ways.dat should be geospatially sorted despite not being geospatially indexed
        // - bsp leaf items can be sorted and diff-encoded. And/or maybe lz4'd?
        // - nodes in ways.dat should be diff-encoded

        Stream openfile(string name) { Directory.CreateDirectory(Path.GetDirectoryName(name)); return File.Open(name, FileMode.Create, FileAccess.Write, FileShare.Read); }
        string hash(string val) => MD5.Create().ComputeHash(val.ToUtf8()).ToHex()[..6].ToLower();
        var filestreams = new AutoDictionary<string, BinaryWriter2>(fname => new BinaryWriter2(openfile(fname)));
        BinaryWriter2 filestream(string path, string name) => filestreams[Path.Combine(dbPath.Concat(path.Split(':').Select(s => s.FilenameCharactersEscape())).Concat(PathUtil.AppendBeforeExtension(name, "." + hash(Path.GetFileNameWithoutExtension(name))).FilenameCharactersEscape()).ToArray())];
        var nodes = new Dictionary<long, ulong>();
        var wayRenumber = new Dictionary<long, uint>();
        var relRenumber = new Dictionary<long, uint>();
        var wayTags = new AutoDictionary<string, string, List<uint>>((_, __) => new List<uint>());
        var nodeTags = new AutoDictionary<string, string, List<ulong>>((_, __) => new List<ulong>());
        var relTags = new AutoDictionary<string, string, List<uint>>((_, __) => new List<uint>());
        var wayAreas = new AutoDictionary<uint, RectArea>(_ => new RectArea());
        var relAreas = new AutoDictionary<uint, RectArea>(_ => new RectArea());
        var relStrings = new StringsCacher(() => filestream("", "rels.strings"));
        long prevWayId = 0, prevWayIdPos = 0;
        long prevRelId = 0, prevRelIdPos = 0;
        var bwWays = filestream("", "ways.dat");
        var bwWaysId = filestream("", "ways.id.dat");
        var bwWaysOsmId = filestream("", "ways.osm_id.dat");
        var bwRels = filestream("", "rels.dat");
        var bwRelsId = filestream("", "rels.id.dat");
        var bwRelsOsmId = filestream("", "rels.osm_id.dat");
        foreach (var el in PbfUtil.ReadPbf(pbfFilename, relsLast: true))
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
                bwWaysId.Write7BitEncodedInt64(bwWays.Position - prevWayIdPos); // to be loaded into RAM during usage
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
                bwRelsId.Write7BitEncodedInt64(bwRels.Position - prevRelIdPos); // to be loaded into RAM during usage
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
        void saveBsp<T>(BinaryWriter2 bw, List<T> items, int depthLimit, int itemsLimit, Func<T, int, int, int, bool> filter, Action<T, BinaryWriter2> writer)
            => new BspWriter<T>(bw, depthLimit, itemsLimit, filter, writer).SaveBsp(items);
        void saveTags<T>(AutoDictionary<string, string, List<T>> tags, string kind, int depthLimit, int itemsLimit, Func<T, int, int, int, bool> filter, Action<T, BinaryWriter2> writer)
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
                        var bw = filestream(tagKey, $"{kind}.tag.{tagKey}={tagVal}.{tags[tagKey][tagVal].Count}.bsp");
                        bw.Write(kind.ToUpper().PadRight(4, ' '));
                        saveBsp(bw, tags[tagKey][tagVal], depthLimit, itemsLimit, filter, writer);
                    }
                }
                var remainingTags = otherValues.SelectMany(tagValue => tags[tagKey][tagValue].Select(n => (tagValue, n))).ToList();
                var bw2 = filestream(tagKey, $"{kind}.tag.{tagKey}.{remainingTags.Count}.bsp");
                bw2.Write(kind.ToUpper().PadRight(4, ' '));
                var strings = remainingTags.Count < 500 ? null : new StringsCacher(() => filestream(tagKey, $"{kind}.tag.{tagKey}.{remainingTags.Count}.strings"));
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
            (t, lat, lon, mask) => { var (ilat, ilon) = LatLon.Unpack(t); return (ilat & mask) == lat && (ilon & mask) == lon; },
            (t, bw) =>
            {
                var (ilat, ilon) = LatLon.Unpack(t);
                bw.Write((ushort)ilat);
                bw.Write((ushort)ilon);
            });

        foreach (var fs in filestreams.Values)
            fs.Dispose();
    }
}

internal class StringsCacher
{
    private Dictionary<string, long> _map = new Dictionary<string, long>();
    private Func<BinaryWriter2> _getWriter;
    private BinaryWriter2 _bwStrings;

    public StringsCacher(Func<BinaryWriter2> getWriter)
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
            {
                _bwStrings = _getWriter();
                _bwStrings.Write("STRN".ToCharArray());
            }
            result = _bwStrings.Position;
            _bwStrings.Write(value);
            _map.Add(value, result);
            return result;
        }
    }
}
