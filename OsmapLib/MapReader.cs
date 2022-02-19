namespace OsmapLib;

public class NodeTagset
{
    public NodeTagsBspReader RemainingReader;
    public Dictionary<string, NodeTagsBspReader> ValueReaders = new();
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
        var files = Directory.GetFiles(dbPath, "*.*", SearchOption.AllDirectories);
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
