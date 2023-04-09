namespace OsmapLib;

public struct LatLon
{
    public int ILat { get; private set; }
    public int ILon { get; private set; }
    public double Lat => ILat / 10_000_000.0;
    public double Lon => ILon / 10_000_000.0;
    public ulong Packed => Pack(ILat, ILon);

    private LatLon(int ilat, int ilon) { ILat = ilat; ILon = ilon; }
    private LatLon(ulong node) { (ILat, ILon) = Unpack(node); }

    public static LatLon FromDeg(double lat, double lon)
    {
        var ilat = checked((int)Math.Round(lat * 10_000_000));
        var ilon = checked((int)Math.Round(lon * 10_000_000));
        return new LatLon(ilat, ilon);
    }

    public static LatLon FromInt(int ilat, int ilon) => new LatLon(ilat, ilon);

    public static LatLon FromPacked(ulong node) => new LatLon(node);

    public static ulong Pack(int ilat, int ilon)
    {
        return ((ulong)(uint)ilat << 32) | (uint)ilon;
    }

    public static (int lat, int lon) Unpack(ulong node)
    {
        int lat = (int)(uint)(node >> 32);
        int lon = (int)(uint)node;
        return (lat, lon);
    }
}
