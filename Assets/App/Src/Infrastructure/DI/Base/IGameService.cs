using System.Threading.Tasks;
using Cysharp.Threading.Tasks;

namespace App.Infrastructure.DI.Base
{
    public interface IGameService
    {
        public UniTask Initialize();

        public UniTask PostInitialize()
        {
            return UniTask.CompletedTask;
        }
    }
}