using QuadroMaps.Pbf;

namespace QuadroMaps.PbfTool;

internal class Program
{
    static void Main(string[] args)
    {
        // this obviously needs some work...
        new PbfConverter().Convert(args[0], args[1]);
    }
}
