namespace Graft.Model;

public sealed record Branch(string Name, string Hash, string ShortHash, bool IsCurrent);
