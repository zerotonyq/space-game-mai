using App.Entities.Config;
using UnityEngine;
using Zenject;

namespace App.Infrastructure.DI
{
    [CreateAssetMenu(fileName = "AppConfigInstaller", menuName = "Installers/AppConfigInstaller")]
    public class AppConfigInstaller : ScriptableObjectInstaller<AppConfigInstaller>
    {
        public WorldCharacterSpawnSettings worldCharacterSpawnSettings;
        public HeroPlanetCinemachineCameraConfig heroPlanetCinemachineCameraConfig;

        public override void InstallBindings()
        {
            if (worldCharacterSpawnSettings)
                Container.Bind<WorldCharacterSpawnSettings>().FromInstance(worldCharacterSpawnSettings).AsSingle();

            if (heroPlanetCinemachineCameraConfig)
                Container.Bind<HeroPlanetCinemachineCameraConfig>()
                    .FromInstance(heroPlanetCinemachineCameraConfig)
                    .AsSingle();
        }
    }
}
