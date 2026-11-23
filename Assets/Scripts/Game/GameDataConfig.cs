public static class GameDataConfig
{
    /// <summary>首次游戏或重置关卡进度时使用的关卡编号。</summary>
    public const int DefaultPlayerLevel = 1;
    /// <summary>新建怪物时的默认移动速度。</summary>
    public const float DefaultMonsterSpeed = 1f;
    /// <summary>新建关卡时的默认网格列数。</summary>
    public const int DefaultLevelWidth = 10;
    /// <summary>新建关卡时的默认网格行数。</summary>
    public const int DefaultLevelHeight = 10;
    /// <summary>关卡网格的行数和列数各自允许的最小值。</summary>
    public const int MinLevelGridSize = 1;
    /// <summary>关卡网格的行数和列数各自允许的最大值。</summary>
    public const int MaxLevelGridSize = 1024;
    /// <summary>场景元素至少覆盖格子的这么大比例，该格才算被占用。</summary>
    public const float SceneElementOccupancyThreshold = 0.2f;
    public const float SceneElementDefaultSizeInCells = 5f / 6f;

    /// <summary>可在信息编辑器中记录设计说明的物品类型。</summary>
    public enum ItemType
    {
        Key = 0,
        Coin = 1,
        TreasureChest = 2,
        Shovel = 3
    }

    public enum ItemQuality { White = 0, Green = 1, Blue = 2, Purple = 3, Red = 4 }

    /// <summary>游戏中的怪物类型。</summary>
    public enum MonsterType
    {
        /// <summary>史莱姆。</summary>
        Slime = 0,
        /// <summary>狼。</summary>
        Wolf = 1
    }

    /// <summary>关卡网格中可放置的场景元素类型。</summary>
    public enum SceneElement
    {
        /// <summary>空格子，无场景元素。</summary>
        None = 0,
        /// <summary>开花的灌木。</summary>
        FloweringBush = 1,
        /// <summary>红浆果灌木。</summary>
        RedBerryBush = 2,
        /// <summary>蓝浆果灌木。</summary>
        BlueBerryBush = 3,
        /// <summary>阔叶植物。</summary>
        LeafyPlant = 4,
        /// <summary>草丛。</summary>
        Grass = 5,
        /// <summary>白色花丛。</summary>
        WhiteFlowers = 6,
        /// <summary>黄色花丛。</summary>
        YellowFlowers = 7,
        /// <summary>紫色水晶簇。</summary>
        PurpleCrystals = 8,
        /// <summary>蘑菇群。</summary>
        Mushrooms = 9,
        /// <summary>覆有苔藓的巨石。</summary>
        MossyBoulder = 10,
        /// <summary>裂缝发光的岩石。</summary>
        GlowingCrackedRock = 11,
        /// <summary>长有水晶的巨石。</summary>
        CrystalBoulder = 12,
        /// <summary>岩石群。</summary>
        RockCluster = 13,
        /// <summary>覆有苔藓的岩石堆。</summary>
        MossyRockStack = 14,
        /// <summary>高耸的岩石。</summary>
        TallRocks = 15,
        /// <summary>大型水晶巨石。</summary>
        LargeCrystalBoulder = 16,
        /// <summary>树桩。</summary>
        TreeStump = 17,
        /// <summary>断裂的树桩。</summary>
        BrokenTreeStump = 18,
        Tree1 = 19,
        Tree2 = 20,
        Tree3 = 21,
        Tree4 = 22,
        Tree5 = 23,
        Tree6 = 24,
        // 25～40 由已发布的地质元素占用；新增树继续使用未占用编号，避免旧关卡数据失效。
        Tree7 = 41,
        Tree8 = 42,
        Tree9 = 43,
        Tree10 = 44,
        Tree11 = 45,
        Tree12 = 46,
        Tree13 = 47,
        Tree14 = 48,
        Tree15 = 49,
        Tree16 = 50
    }

    /// <summary>关卡地质类型，保留现有关卡和 Sprite 数组使用的编号。</summary>
    public enum GeologyElement
    {
        None = 0,
        // geology.png uses four variants each of grass, dirt, stone and mossy dirt.
        GrassTile1 = 25,
        GrassTile2 = 26,
        GrassTile3 = 27,
        GrassTile4 = 28,
        DirtTile1 = 29,
        DirtTile2 = 30,
        DirtTile3 = 31,
        DirtTile4 = 32,
        StoneTile1 = 33,
        StoneTile2 = 34,
        StoneTile3 = 35,
        StoneTile4 = 36,
        MossyDirtTile1 = 37,
        MossyDirtTile2 = 38,
        MossyDirtTile3 = 39,
        MossyDirtTile4 = 40
    }

    public static bool IsGeology(int elementId)
    {
        return elementId >= (int)GeologyElement.GrassTile1 && elementId <= (int)GeologyElement.MossyDirtTile4;
    }

    public static bool IsTree(int elementId)
    {
        return elementId >= (int)SceneElement.Tree1 && elementId <= (int)SceneElement.Tree6 ||
               elementId >= (int)SceneElement.Tree7 && elementId <= (int)SceneElement.Tree16;
    }

    public static int GetTreeElementId(int treeNumber)
    {
        return treeNumber <= 6
            ? (int)SceneElement.Tree1 + treeNumber - 1
            : (int)SceneElement.Tree7 + treeNumber - 7;
    }

    public static string GetElementName(int elementId)
    {
        return IsGeology(elementId) ? ((GeologyElement)elementId).ToString() : ((SceneElement)elementId).ToString();
    }
}
