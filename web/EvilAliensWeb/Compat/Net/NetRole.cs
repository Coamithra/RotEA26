namespace EvilAliensWeb.Compat
{
    // Parsed from ?net=host / ?net=join (DebugFlags). None = the co-op net layer is never
    // constructed -- a plain boot stays byte-identical single-player. In the Compat
    // namespace (not Compat.Net) because DebugFlags carries it.
    public enum NetRole
    {
        None,
        Host,
        Join
    }
}
