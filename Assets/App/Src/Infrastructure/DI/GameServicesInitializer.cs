using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Zenject;

namespace App.Infrastructure.DI
{
    public class GameServicesInitializer : IInitializable
    {
        private readonly List<IGameService> _gameServices;
        private bool _isInitializing;
        private bool _isInitialized;

        public GameServicesInitializer(List<IGameService> gameServices)
        {
            _gameServices = gameServices;
        }

        public void Initialize()
        {
            if (_isInitializing || _isInitialized)
                return;

            InitializeAsync().Forget();
        }

        private async UniTaskVoid InitializeAsync()
        {
            _isInitializing = true;
            try
            {
                for (var i = 0; i < _gameServices.Count; i++)
                    await _gameServices[i].Initialize();

                _isInitialized = true;
            }
            catch (System.Exception exception)
            {
                Debug.LogException(exception);
            }
            finally
            {
                _isInitializing = false;
            }
        }
    }
}
