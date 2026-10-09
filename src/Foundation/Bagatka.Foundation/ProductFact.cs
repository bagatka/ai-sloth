namespace Bagatka.Foundation;

/// <summary>A fact about a product event: a kind or outcome as text, a count or duration in seconds, or a yes or no.</summary>
public union ProductFact(string, double, bool);
