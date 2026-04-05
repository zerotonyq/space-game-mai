using System.Collections.Generic;
using App.Infrastructure.DI.Base;
using Cysharp.Threading.Tasks;
using Zenject;

namespace App.Infrastructure.DI
{
    public class GameServicesInitializer
    {
        [Inject] private readonly List<IGameService> _gameServices;
        
        [Inject]
        public async UniTask Initialize()
        {
            await InitializeAsync();
            await PostInitializeAsync();
        }

        private async UniTask PostInitializeAsync()
        {
            foreach (var service in _gameServices)
                await service.PostInitialize();
        }

        private async UniTask InitializeAsync()
        {
            foreach (var service in _gameServices)
                await service.Initialize();
        }
    }
}