using UnityEngine;

/// <summary>
/// Ajusta el tamaño del plano para que cubra exactamente
/// el área visible de la cámara ortográfica.
/// </summary>
[ExecuteAlways]
public class PlaneAutoFit : MonoBehaviour
{
    public Camera targetCamera;

    void Update()
    {
        if (targetCamera == null) return;
        if (!targetCamera.orthographic) return;

        float height = targetCamera.orthographicSize * 2f;   // alto en unidades
        float width = height * targetCamera.aspect;          // ancho en unidades

        // Un Plane de Unity mide 10x10 unidades cuando su escala es (1,1,1).
        float scaleX = width / 10f;
        float scaleZ = height / 10f;

        transform.localScale = new Vector3(scaleX, 1f, scaleZ);
        transform.position = new Vector3(0f, 0f, 0f);        // centrado
        transform.rotation = Quaternion.Euler(0f, 0f, 0f);   // horizontal
    }
}
