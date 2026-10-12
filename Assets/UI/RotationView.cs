using UnityEngine;

public class RotationView : MonoBehaviour
{
    [SerializeField] private Vector3 rotationSpeed = new Vector3(0f, 30f, 0f);

    void Update()
    {
        transform.Rotate(rotationSpeed * Time.deltaTime);
    }
}