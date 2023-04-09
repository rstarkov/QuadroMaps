using QuadroMaps.Core;

namespace QuadroMaps.GMaps;

internal static class ExtensionMethods
{
    public static double WrongAngDist(this LatLon pt1, LatLon pt2)
    {
        // this is obviously very wrong but it's okay enough for algorithms in this library
        return Math.Sqrt((pt2.Lon - pt1.Lon) * (pt2.Lon - pt1.Lon) + (pt2.Lat - pt1.Lat) * (pt2.Lat - pt1.Lat));
    }
}
