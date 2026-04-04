using Cysharp.Threading.Tasks;

namespace App.Infrastructure.DI
{
    public interface IGameService
    {
        public UniTask Initialize();
    }
}