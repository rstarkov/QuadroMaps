namespace OsmapLib;

public class RectArea
{
    public int LatMin = int.MaxValue, LatMax, LonMin, LonMax;

    public (int bits, int mask) SameBitsLat => getBits(LatMin, LatMax);
    public (int bits, int mask) SameBitsLon => getBits(LonMin, LonMax);

    private (int bits, int mask) getBits(int lonMin, int lonMax)
    {
        if (lonMin == lonMax)
            return (lonMin, unchecked((int)0xFFFF_FFFF));
        int mask = unchecked((int)0x8000_0000);
        while ((lonMin & mask) == (lonMax & mask))
            mask >>= 1; // sign-extended
        mask <<= 1;
        return (lonMin & mask, mask);
    }

    public void AddLatLon(LatLon latlon)
    {
        AddLatLon(latlon.ILat, latlon.ILon);
    }

    private void AddLatLon(int ilat, int ilon)
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

    public bool Contains(LatLon pt) => pt.ILat >= LatMin && pt.ILat <= LatMax && pt.ILon >= LonMin && pt.ILon <= LonMax;
    public bool Contains(double lat, double lon) { var ilat = lat * 10_000_000; var ilon = lon * 10_000_000; return ilat >= LatMin && ilat <= LatMax && ilon >= LonMin && ilon <= LonMax; }
}

