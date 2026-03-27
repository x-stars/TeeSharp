sealed record class ConsList<T>(T Head, ConsList<T>? Tail)
{
	public static implicit operator ConsList<T>(
		(T Head, ConsList<T>? Tail) node) => new(node.Head, node.Tail);
	public override string ToString() => $"({this.Head}, {this.Tail})";
}

static class ConsList
{
	public static ConsList<T>? Create<T>(params ReadOnlySpan<T> items)
	{
		var result = (ConsList<T>?)null;
		for (var index = items.Length - 1; index >= 0; index--)
		{
			result = (items[index], result);
		}
		return result;
	}
	public static ConsList<T>? Create<T>(params IEnumerable<T> items)
	{
		ArgumentNullException.ThrowIfNull(items);
		var result = (ConsList<T>?)null;
		foreach (var item in items.Reverse())
		{
			result = (item, result);
		}
		return result;
	}
	public static IEnumerable<T> AsEnumerable<T>(this ConsList<T>? source)
	{
		for (var current = source; current is var (head, tail); current = tail)
		{
			yield return head;
		}
	}
	public static IEnumerator<T> GetEnumerator<T>(this ConsList<T>? source) =>
		source.AsEnumerable().GetEnumerator();
}
