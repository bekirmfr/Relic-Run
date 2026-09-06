using UnityEngine;

namespace GameLift.Utility
{
    public class AutoRotator : MonoBehaviour
    {
        [SerializeField] private Vector3 _rotationSpeed = new Vector3(0f, 30f, 0f);

        void Update()
        {
            transform.Rotate(_rotationSpeed * Time.deltaTime);
        }
    }
}