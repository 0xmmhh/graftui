namespace Graft.Git;

public sealed record Branch(string Name, string ShortHash, bool IsCurrent);
