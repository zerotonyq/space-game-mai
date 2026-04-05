namespace App.Planets.Placement
{
    public readonly struct PlanetPlacementContext
    {
        public PlanetPlacementContext(int totalLayers, float coreRadiusUnits, int layerIndex, int layerCount)
        {
            TotalLayers = totalLayers;
            CoreRadiusUnits = coreRadiusUnits;
            LayerIndex = layerIndex;
            LayerCount = layerCount;
        }

        public int TotalLayers { get; }
        public float CoreRadiusUnits { get; }
        public int LayerIndex { get; }
        public int LayerCount { get; }

        public float LayerPercentile
        {
            get
            {
                if (LayerCount <= 1)
                    return 50f;

                return LayerIndex / (float)(LayerCount - 1) * 100f;
            }
        }
    }
}
