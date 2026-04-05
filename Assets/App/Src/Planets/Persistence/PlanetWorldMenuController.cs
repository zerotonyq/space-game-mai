using UnityEngine;

namespace App.Planets.Persistence
{
    // Scene context for menu runtime service. Holds scene references and defaults.
    public class PlanetWorldMenuController : MonoBehaviour
    {
        [Header("Dependencies")]
        [SerializeField] private PlanetWorldManager worldManager;

        [Header("Canvas Controllers")]
        [SerializeField] private WorldMenuCanvasController menuCanvasController;
        [SerializeField] private WorldGameplayCanvasController gameplayCanvasController;
        [SerializeField] private WorldLoadingCanvasController loadingCanvasController;

        [Header("New Game")]
        [SerializeField] private string newWorldIdPrefix = "world";
        [SerializeField] private PlanetWorldManager.WorldSize defaultNewGameWorldSize = PlanetWorldManager.WorldSize.Medium;

        [Header("Exit World")]
        [SerializeField] private bool saveBeforeUnloadCurrentWorld = true;

        public PlanetWorldManager WorldManager => worldManager;
        public WorldMenuCanvasController MenuCanvasController => menuCanvasController;
        public WorldGameplayCanvasController GameplayCanvasController => gameplayCanvasController;
        public WorldLoadingCanvasController LoadingCanvasController => loadingCanvasController;
        public string NewWorldIdPrefix => newWorldIdPrefix;
        public PlanetWorldManager.WorldSize DefaultNewGameWorldSize => defaultNewGameWorldSize;
        public bool SaveBeforeUnloadCurrentWorld => saveBeforeUnloadCurrentWorld;
    }
}
