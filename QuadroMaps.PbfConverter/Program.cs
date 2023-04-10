using QuadroMaps.Pbf;

namespace QuadroMaps.PbfTool;

internal class Program
{
    static void Main(string[] args)
    {
        // this obviously needs some work...
        var start = DateTime.UtcNow;
        new PbfConverter(PbfUtil.ReadPbf(args[0]), args[1]).Convert();
        Console.WriteLine($"Done in {(DateTime.UtcNow - start).TotalSeconds:0.0} sec");
    }
}
