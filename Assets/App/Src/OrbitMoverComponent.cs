using UnityEngine;

namespace App.Src
{
    public class OrbitMoverComponent : MonoBehaviour
    {
        [SerializeField] private float _speed;
        [SerializeField] private Transform _target;
        [SerializeField] private OrbitDirectionType _orbitDirectionType;
    
        void Update()
        {
            var currentDirection = (transform.position - _target.position).normalized;
            var newDirection = Quaternion.AngleAxis(_speed, Vector3.forward) * currentDirection;
            
            transform.position = _target.position + newDirection * 3f;
        }
    }

    internal enum OrbitDirectionType
    {
        None,
        Clockwise,
        CounterClockwise,
    }
}