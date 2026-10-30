namespace Portaler
{
    /// <summary>Portal categories (shared by everybody). 0 = none. The order is fixed: it is stored by number.</summary>
    public static class Categories
    {
        public static readonly string[] Keys = { "none", "base", "mine", "farm", "trader", "boss", "danger", "dungeon", "explore" };
        public static readonly string[] Names = { "None", "Base", "Mine", "Farm", "Trader", "Boss", "Danger", "Dungeon", "Explore" };
        public const int Count = 9;

        public static int Clamp(int c) => c < 0 || c >= Count ? 0 : c;
    }
}
