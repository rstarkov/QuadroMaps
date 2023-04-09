using System.Text.RegularExpressions;

namespace QuadroMaps.Core;

public class NodeTagset
{
    public NodeTagsQuadtreeReader RemainingReader;
    public Dictionary<string, NodeTagsQuadtreeReader> ValueReaders = new();
    public int TotalCount;
}

public class MapReader
{
    public string DbPath { get; private set; }

    private Dictionary<string, NodeTagset> _nodeTags;
    //private Dictionary<string, TagKeyInfo> _wayTags;
    //private Dictionary<string, TagKeyInfo> _relTags;
    private Dictionary<uint, long> _wayIds;
    private Dictionary<uint, long> _relIds;
    private Dictionary<uint, string> _relStrings;

    public MapReader(string dbPath)
    {
        DbPath = dbPath;
        foreach (var file in new DirectoryInfo(dbPath).GetFiles("*.*", SearchOption.AllDirectories))
        {
            Match match;
            if ((match = Regex.Match(file.FullName.UnescapeFilename(), @"^node\.tag\.(?<tagname>[^=]*)=(?<tagvalue>[^=]*)\.(?<count>\d+)\.[0-9a-f]+\.qtr$")).Success)
            {
                _nodeTags[match.Groups["tagname"].Value].ValueReaders[match.Groups["tagvalue"].Value] = new NodeTagsQuadtreeReader(file.FullName, null, match.Groups["tagvalue"].Value);
            }
        }
    }

    public class Polyline
    {
        public LatLon[] Points;
        public string TagValue;
    }

    //public IEnumerable<Polyline> GetWays(RectArea area, string tagKey)
    //{
    //}

    //public IEnumerable<Polyline> GetWays(RectArea area, string tagKey, string tagValue)
    //{
    //}

    //public IEnumerable<Way> GetPolygons(RectArea area, string tagKey, string tagValue)
    //{
    //}

    static (uint, double, double) latlon2qtree(double lat, double lon)
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
