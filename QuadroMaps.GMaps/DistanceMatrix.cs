using Newtonsoft.Json.Linq;
using QuadroMaps.Core;
using RT.KitchenSink.Geometry;
using RT.Util;
using RT.Util.Collections;
using RT.Util.ExtensionMethods;
using RT.Util.Geometry;

namespace QuadroMaps.GMaps;

public class TimedPt
{
    public LatLon Point;
    public double Time;
}

public class DistanceMatrix
{
    public string ApiKey;

    public LatLon Origin;
    public List<TimedPt> Points = new List<TimedPt>();

    public void Spread(float step)
    {
        if (Points.Count == 0)
            Points.Add(new TimedPt { Point = Origin, Time = 0 });

        var points = new List<LatLon>();
        foreach (var tpt in Points.OrderBy(p => Rnd.NextDouble()))
        {
            if (tpt.Time > 20 * 3600)
                continue;
            void tryAddPt(double angle)
            {
                var pt = LatLon.FromDeg(lon: tpt.Point.Lon + (step * Math.Cos(angle)), lat: tpt.Point.Lat + (step * Math.Sin(angle)));
                var dist = Points.Select(p => p.Point).Concat(points).Select(p => p.WrongAngDist(pt)).MinOrDefault(double.MaxValue);
                if (dist < step * 0.9)
                    return;
                points.Add(pt);
            }
            tryAddPt(Rnd.NextDouble(0, 2 * Math.PI));
            tryAddPt(Rnd.NextDouble(0, 2 * Math.PI));
            tryAddPt(Rnd.NextDouble(0, 2 * Math.PI));
            tryAddPt(Rnd.NextDouble(0, 2 * Math.PI));
            tryAddPt(Rnd.NextDouble(0, 2 * Math.PI));
            if (points.Count > 25)
                break;
        }
        while (points.Count > 25)
            points.RemoveAt(points.Count - 1);
        Query(points);
    }

    public List<TriangleD> SpreadBoundaryDelaunay(double time)
    {
        var pdInside = Points.ToDictionary(p => new PointD(p.Point.Lon, p.Point.Lat), p => p.Time <= time);
        var triangulation = Triangulate.Delaunay(pdInside.Keys);
        var edgeTriangles = triangulation.Where(t => (pdInside[t.V1] || pdInside[t.V2] || pdInside[t.V3]) && (!pdInside[t.V1] || !pdInside[t.V2] || !pdInside[t.V3])).ToList();
        var newPoints = edgeTriangles.Select(t => t.Centroid).Select(c => LatLon.FromDeg(lon: (float)c.X, lat: (float)c.Y)).ToList();
        // remove in chunks - this is "greedy" and might remove points that are all too close to each other, but with some removed they would be desirable. But this is much faster.
        while (newPoints.Count > 150)
        {
            newPoints = newPoints
                .OrderByDescending(p => newPoints.Where(np => p != np).Concat(Points.Select(pt => pt.Point)).Min(pt => pt.WrongAngDist(p)))
                .Take(Math.Max(150, (int)(newPoints.Count * 0.8)))
                .ToList();
        }
        // remove points one at a time
        while (newPoints.Count > Math.Min(25, edgeTriangles.Count / 4))
        {
            var worstPoint = newPoints.OrderBy(p => newPoints.Where(np => p != np).Concat(Points.Select(pt => pt.Point)).Min(pt => pt.WrongAngDist(p))).First();
            newPoints.Remove(worstPoint);
        }
        Query(newPoints);
        return edgeTriangles;
    }

    public double SpreadBoundaryVoronoi(double time)
    {
        var minlat = Points.Min(p => p.Point.Lat) - 1;
        var minlon = Points.Min(p => p.Point.Lon) - 1;
        var sites = Points.Select(p => new PointD(p.Point.Lon - minlon, p.Point.Lat - minlat)).ToArray();
        var vd = VoronoiDiagram.GenerateVoronoiDiagram(sites, 360, 360, VoronoiDiagramFlags.IncludeEdgePolygons);
        var pointSites = new AutoDictionary<PointD, HashSet<int>>(_ => new HashSet<int>());
        for (int site = 0; site < vd.Polygons.Length; site++)
            foreach (var v in vd.Polygons[site].Vertices)
                pointSites[v].Add(site);
        var points = new List<(LatLon, double)>();
        foreach (var ps in pointSites)
        {
            if (ps.Value.Count != 3)
                continue;
            if (!ps.Value.Any(site => Points[site].Time <= time) || !ps.Value.Any(site => Points[site].Time > time))
                continue; // it needs to have some points inside and some points outside the time limit
            var pt = LatLon.FromDeg(lon: ps.Key.X + minlon, lat: ps.Key.Y + minlat);
            var dist = (ps.Key - sites[ps.Value.First()]).Abs(); // it's equidistant from all three, as that's the whole point of voronoi
            points.Add((pt, dist));
        }
        var newPoints = new List<LatLon>();
        foreach (var pt in points.OrderByDescending(p => p.Item2))
        {
            if (newPoints.Any(np => np.WrongAngDist(pt.Item1) < pt.Item2))
                continue;
            newPoints.Add(pt.Item1);
            if (newPoints.Count >= 25)
                break;
        }
        Query(newPoints);
        return points.Average(p => p.Item2);
    }

    public void RefineBoundaryBad(double time)
    {
        // for each point inside, find nearest point outside
        // take the median of that as the current "boundary thickness"
        // from each inside point, go to every outside point within 2x boundary thickness
        //   add the halfway point so long as there isn't another point within half the boundary thickness
        // for each inside point that has an outside pair, make sure its nearest neighbour is at most boundary/2 away
        var inside = Points.Where(p => p.Time <= time).ToList();
        var outside = Points.Where(p => p.Time > time).ToList();
        inside.Shuffle();
        outside.Shuffle(); // so that if we don't query every point the whole boundary is partially filled in throughout
        var mindists = inside.Select(pi => outside.Select(po => po.Point.WrongAngDist(pi.Point)).Min()).Order().ToList();
        var boundary = mindists[mindists.Count / 2];
        var points = new List<LatLon>();
        var pairs = inside.SelectMany(pi => outside.Where(po => po.Point.WrongAngDist(pi.Point) < boundary * 1.5).Select(po => (pi, po)));
        foreach (var (pi, po) in pairs)
        {
            var pt = LatLon.FromDeg(lat: (pi.Point.Lat + po.Point.Lat) / 2, lon: (pi.Point.Lon + po.Point.Lon) / 2);
            if (points.Select(p => pt.WrongAngDist(p)).MinOrDefault(double.MaxValue) < boundary / 2)
                continue; // too close to another new point to be queried
            if (Points.Select(p => pt.WrongAngDist(p.Point)).MinOrDefault(double.MaxValue) < 0.8 * boundary / 2)
                continue; // too close to another already existing point
            points.Add(pt);
        }
        // subdivide the inside and outside edge
        subedge(pairs.Select(p => p.pi.Point).Distinct().ToList());
        subedge(pairs.Select(p => p.po.Point).Distinct().ToList());
        void subedge(List<LatLon> edge)
        {
            foreach (var pe in edge)
            {
                var nearest = edge.Where(p => p != pe).OrderBy(p => p.WrongAngDist(pe)).Take(3);
                foreach (var pn in nearest)
                {
                    var pt = LatLon.FromDeg(lat: (pe.Lat + pn.Lat) / 2, lon: (pe.Lon + pn.Lon) / 2);
                    if (points.Select(p => pt.WrongAngDist(p)).MinOrDefault(double.MaxValue) < 0.8 * boundary / 2)
                        continue; // too close to another new point to be queried
                    if (Points.Select(p => pt.WrongAngDist(p.Point)).MinOrDefault(double.MaxValue) < 0.8 * boundary / 2)
                        continue; // too close to another already existing point
                    points.Add(pt);
                }
            }
        }
        Query(points);
    }

    private HttpClient _hc = new HttpClient();

    public void Query(List<LatLon> points)
    {
        var url = $"https://maps.googleapis.com/maps/api/distancematrix/json?origins={Origin.Lat},{Origin.Lon}&destinations={points.Select(p => p.Lat + "," + p.Lon).JoinString("|")}&key={ApiKey}";
        var resp = _hc.GetStringAsync(url).GetAwaiter().GetResult();
        dynamic json = JObject.Parse(resp);
        dynamic results = json.rows[0].elements;
        if (results.Count != points.Count)
            throw new Exception();
        for (int i = 0; i < (int)results.Count; i++)
        {
            var time = results[i].status == "OK" ? (double)results[i].duration.value : double.MaxValue;
            Points.Add(new TimedPt { Point = points[i], Time = time });
        }
    }
}
