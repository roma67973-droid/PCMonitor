namespace PCMonitor.ViewModels.Infrastructure;

/// <summary>Кольцевой буфер последних N значений для мини-графиков.</summary>
public sealed class RingBuffer
{
    private readonly double[] _items;
    private int _next;
    private int _count;

    public RingBuffer(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _items = new double[capacity];
    }

    public int Count => _count;

    public void Add(double value)
    {
        _items[_next] = value;
        _next = (_next + 1) % _items.Length;
        _count = Math.Min(_count + 1, _items.Length);
    }

    /// <summary>Значения от самого старого к самому новому. Каждый вызов — новый массив (для смены привязки).</summary>
    public double[] ToArray()
    {
        var result = new double[_count];
        var start = (_next - _count + _items.Length) % _items.Length;
        for (var i = 0; i < _count; i++)
        {
            result[i] = _items[(start + i) % _items.Length];
        }

        return result;
    }
}
