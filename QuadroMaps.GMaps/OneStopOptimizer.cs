using Newtonsoft.Json.Linq;
using QuadroMaps.Core;
using RT.KitchenSink.Geometry;
using RT.Util;
using RT.Util.ExtensionMethods;
using RT.Util.Geometry;

namespace QuadroMaps.GMaps;

public class OneStopSample
{
    public LatLon Point;
    public double Time1;
    public double Time2;
    public double TotalTime => Time1 + Time2;
}

public class OneStopOptimizer
{
    public string ApiKey;

    public LatLon Origin1, Origin2;
    public string Options1, Options2;
    public bool Leaving1, Leaving2; // true = leaving origin, false = arriving at origin

    public List<OneStopSample> Samples = new List<OneStopSample>();

    public void Grid(float radius, LatLon pt1, LatLon pt2)
    {
        var latMin = Math.Min(pt1.Lat, pt2.Lat) + radius * 1.1f;
        var latMax = Math.Max(pt1.Lat, pt2.Lat) - radius * 1.1f;
        var lonMin = Math.Min(pt1.Lon, pt2.Lon) + radius * 1.1f;
        var lonMax = Math.Max(pt1.Lon, pt2.Lon) - radius * 1.1f;

        var toQuery = new List<LatLon>();
        void addQuery(LatLon? point)
        {
            if (point != null)
                toQuery.Add(point.Value);
            if (toQuery.Count >= 25 || point == null)
            {
                Query(toQuery);
                toQuery.Clear();
            }
        }

        for (var lat = latMin; lat <= latMax; lat += radius * 0.7f)
            for (var lon = lonMin; lon <= lonMax; lon += radius * 0.7f)
            {
                var pt = LatLon.FromDeg(Rnd.NextDouble(lat - radius, lat + radius), Rnd.NextDouble(lon - radius, lon + radius));
                if (!Samples.Select(s => s.Point).Concat(toQuery).Any(p => p.WrongAngDist(pt) < radius))
                    addQuery(pt);
            }
        addQuery(null);
    }

    public void SubdivideLargestDeltas(float radius, int count)
    {
        var minLat = Samples.Min(s => s.Point.Lat) - 1;
        var minLon = Samples.Min(s => s.Point.Lon) - 1;
        var pts = Samples.ToDictionary(s => new PointD(s.Point.Lon - minLon, s.Point.Lat - minLat));
        var sites = pts.Keys.ToArray();
        var vd = VoronoiDiagram.GenerateVoronoiDiagram(sites, 360, 360);
        var largest = vd.Edges
            .Select(e => (e, p1: pts[sites[e.siteA]], p2: pts[sites[e.siteB]]))
            .OrderByDescending(x => Math.Abs(x.p1.TotalTime - x.p2.TotalTime))
            .Where(x => x.p1.Point.WrongAngDist(x.p2.Point) >= 2 * radius)
            .Take(count).ToList();
        var points = largest.Select(x => LatLon.FromDeg(lat: (x.p1.Point.Lat + x.p2.Point.Lat) / 2 + Rnd.NextDouble(-radius / 5, radius / 5), lon: (x.p1.Point.Lon + x.p2.Point.Lon) / 2 + Rnd.NextDouble(-radius / 5, radius / 5))).ToList();
        Query(points);
    }

    private HttpClient _hc = new();

    public void Query(List<LatLon> points)
    {
        if (points.Count == 0)
            return;
        var ptsarg = points.Select(p => p.Lat + "," + p.Lon).JoinString("|");
        var url1 = $"https://maps.googleapis.com/maps/api/distancematrix/json?{(Leaving1 ? "origins" : "destinations")}={Origin1.Lat},{Origin1.Lon}&{(Leaving1 ? "destinations" : "origins")}={ptsarg}&key={ApiKey}{Options1}";
        var url2 = $"https://maps.googleapis.com/maps/api/distancematrix/json?{(Leaving2 ? "origins" : "destinations")}={Origin2.Lat},{Origin2.Lon}&{(Leaving2 ? "destinations" : "origins")}={ptsarg}&key={ApiKey}{Options2}";
        Console.WriteLine(url1);
        var resp1 = _hc.GetStringAsync(url1).GetAwaiter().GetResult();
        dynamic results1 = Leaving1 ? ((dynamic)JObject.Parse(resp1)).rows[0].elements : ((IEnumerable<dynamic>)((dynamic)JObject.Parse(resp1)).rows).Select((Func<dynamic, dynamic>)(r => r.elements[0])).ToList();
        if (results1.Count != points.Count)
            throw new Exception();
        Console.WriteLine(url2);
        var resp2 = _hc.GetStringAsync(url2).GetAwaiter().GetResult();
        dynamic results2 = Leaving2 ? ((dynamic)JObject.Parse(resp2)).rows[0].elements : ((IEnumerable<dynamic>)((dynamic)JObject.Parse(resp2)).rows).Select((Func<dynamic, dynamic>)(r => r.elements[0])).ToList();
        if (results2.Count != points.Count)
            throw new Exception();
        for (int i = 0; i < points.Count; i++)
        {
            var time1 = results1[i].status == "OK" ? (double)results1[i].duration.value : double.NaN;
            var time2 = results2[i].status == "OK" ? (double)results2[i].duration.value : double.NaN;
            Samples.Add(new OneStopSample { Point = points[i], Time1 = time1, Time2 = time2 });
        }
    }
}
