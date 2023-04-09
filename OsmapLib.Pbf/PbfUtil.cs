using System.IO.Compression;
using OsmSharp;
using OsmSharp.Streams;
using OsmSharp.Tags;

namespace OsmapLib.Generator;

public static class PbfUtil
{
    public static IEnumerable<OsmGeo> ReadPbf(string pbfFilename, bool relsLast = false)
    {
        Console.WriteLine("ReadPbf: reading rels...");
        if (relsLast)
        {
            foreach (var item in pbfInner(pbfFilename))
                yield return item;
            yield break;
        }

        var relsFilename = pbfFilename + ".rels";
        if (!File.Exists(relsFilename))
        {
            using var writer = new BinaryWriter(new GZipStream(File.Open(relsFilename, FileMode.Create, FileAccess.Write, FileShare.Read), CompressionLevel.Optimal));
            foreach (var rel in pbfInner(pbfFilename).OfType<Relation>())
            {
                writer.Write7BitEncodedInt64(rel.Id.Value);
                writer.Write7BitEncodedInt(rel.Members.Length);
                foreach (var member in rel.Members)
                {
                    writer.Write7BitEncodedInt64(member.Id);
                    writer.Write7BitEncodedInt((int)member.Type);
                    writer.Write(member.Role);
                }
                writer.Write7BitEncodedInt(rel.Tags.Count);
                foreach (var tag in rel.Tags)
                {
                    writer.Write(tag.Key);
                    writer.Write(tag.Value);
                }
            }
            writer.Write7BitEncodedInt64(-47);
        }
        {
            using var reader = new BinaryReader(new GZipStream(File.Open(relsFilename, FileMode.Open, FileAccess.Read, FileShare.Read), CompressionMode.Decompress));
            while (true)
            {
                var id = reader.Read7BitEncodedInt64();
                if (id == -47)
                    break;
                var rel = new Relation();
                rel.Id = id;
                rel.Members = new RelationMember[reader.Read7BitEncodedInt()];
                for (int i = 0; i < rel.Members.Length; i++)
                {
                    rel.Members[i] = new RelationMember();
                    rel.Members[i].Id = reader.Read7BitEncodedInt64();
                    rel.Members[i].Type = (OsmGeoType)reader.Read7BitEncodedInt();
                    rel.Members[i].Role = reader.ReadString();
                }
                rel.Tags = new TagsCollection();
                var tagCount = reader.Read7BitEncodedInt();
                for (int i = 0; i < tagCount; i++)
                {
                    var key = reader.ReadString();
                    rel.Tags.Add(key, reader.ReadString());
                }
                yield return rel;
            }
        }
        Console.WriteLine("ReadPbf: reading rest of pbf...");
        foreach (var item in pbfInner(pbfFilename))
            if (item is not Relation)
                yield return item;
    }

    private static IEnumerable<OsmGeo> pbfInner(string pbfFilename)
    {
        var last = DateTime.UtcNow;
        using var pbfStream = File.Open(pbfFilename, FileMode.Open, FileAccess.Read, FileShare.Read);
        foreach (var item in new PBFOsmStreamSource(pbfStream))
        {
            yield return item;
            if ((DateTime.UtcNow - last) > TimeSpan.FromSeconds(1))
            {
                Console.WriteLine($"ReadPbf: processed {pbfStream.Position:#,0} of {pbfStream.Length:#,0} bytes ({pbfStream.Position * 100.0 / pbfStream.Length:0.0}%)");
                last = DateTime.UtcNow;
            }
        }
    }
}
