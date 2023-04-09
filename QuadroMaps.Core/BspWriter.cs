namespace QuadroMaps.Core;

public class BspWriter<T>
{
    public BspWriter(BinaryWriter2 bw, int depthLimit, int itemsCountLimit, Func<T, int, int, int, bool> filter, Action<T, BinaryWriter2> write)
    {
        _bw = bw;
        _depthLimit = depthLimit;
        _itemsCountLimit = itemsCountLimit;
        _filter = filter;
        _write = write;
    }

    private BinaryWriter2 _bw;
    private int _depthLimit;
    private int _itemsCountLimit;
    private Func<T, int, int, int, bool> _filter;
    private Action<T, BinaryWriter2> _write;

    public void SaveBsp(List<T> items)
    {
        saveBsp(items, 0, 0, 0);
    }

    private void saveBsp(List<T> items, int depth, int latBits, int lonBits)
    {
        var mask = ~((1 << (32 - depth)) - 1);
        if (depth > 0)
        {
            int lat = latBits << (32 - depth);
            int lon = lonBits << (32 - depth);
            items = items.Where(t => _filter(t, lat, lon, mask)).ToList();
        }
        if (depth == _depthLimit || items.Count == 0 || items.Count < _itemsCountLimit)
        {
            if (depth != _depthLimit)
                _bw.Write(uint.MaxValue); // marker of early exit from depth recursion
            _bw.Write7BitEncodedInt(items.Count);
            foreach (var item in items)
                _write(item, _bw);
            return;
        }

        depth++;
        mask = ~((1 << (32 - depth)) - 1);
        latBits <<= 1;
        lonBits <<= 1;

        var backpatch = _bw.Position;
        _bw.Write(0); // 01
        _bw.Write(0); // 10
        _bw.Write(0); // 11

        saveBsp(items, depth, latBits, lonBits);
        _bw.Position = backpatch;
        _bw.Write(checked((uint)_bw.Length));
        _bw.Position = _bw.Length;
        saveBsp(items, depth, latBits, lonBits | 1);
        _bw.Position = backpatch + 4;
        _bw.Write(checked((uint)_bw.Length));
        _bw.Position = _bw.Length;
        saveBsp(items, depth, latBits | 1, lonBits);
        _bw.Position = backpatch + 8;
        _bw.Write(checked((uint)_bw.Length));
        _bw.Position = _bw.Length;
        saveBsp(items, depth, latBits | 1, lonBits | 1);
    }
}
