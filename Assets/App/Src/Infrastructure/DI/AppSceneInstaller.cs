using App.Entities;
using App.Planets.GfxGen;
using App.Planets.GfxGen.Persistence;
using Zenject;

namespace App.Infrastructure.DI
{
    public class AppSceneInstaller : MonoInstaller
    {
        public override void InstallBindings()
        {
            Container.BindInterfacesAndSelfTo<GameServicesInitializer>().AsSingle().NonLazy();
            
            
            Container.Bind<PlanetWorldService>().FromComponentInHierarchy().AsSingle();
            Container.Bind<PlanetRuntimeGenerationFactory>().FromComponentInHierarchy().AsSingle();
            Container.BindInterfacesAndSelfTo<PlanetWorldManager>().FromComponentInHierarchy().AsSingle();

            Container.Bind<PlanetWorldMenuController>().FromComponentInHierarchy().AsSingle();
            Container.Bind<WorldMenuCanvasController>().FromComponentInHierarchy().AsSingle();
            Container.Bind<WorldGameplayCanvasController>().FromComponentInHierarchy().AsSingle();
            Container.Bind<WorldLoadingCanvasController>().FromComponentInHierarchy().AsSingle();
            Container.BindInterfacesAndSelfTo<PlanetWorldMenuRuntimeService>().AsSingle().NonLazy();

            Container.BindInterfacesAndSelfTo<WorldCharacterSpawnRuntimeService>().AsSingle().NonLazy();

            Container.BindInterfacesAndSelfTo<HeroPlanetCinemachineCameraRuntimeService>().AsSingle().NonLazy();
            Container.BindInterfacesAndSelfTo<HeroPlanetSwitchRuntimeService>().AsSingle().NonLazy();
        }
    }
}
