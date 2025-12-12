using UnityEngine;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System;

/// <summary>
/// Gestor de interacción de mano que recibe datos por UDP desde Python
/// y controla un cursor 3D que puede:
/// - Moverse en un plano XZ (vista desde arriba).
/// - Seleccionar y arrastrar objetos con tag "Interactable".
/// - Unir/pegar objetos cercanos (glue).
/// - Rotar el objeto seleccionado según ángulos enviados.
/// - Aplicar zoom (escala) al objeto seleccionado.
/// - Evitar que el objeto salga del plano GroundPlane.
/// </summary>
public class HandManager : MonoBehaviour
{
    [Header("Red (UDP)")]
    public int port = 5052;
    private UdpClient client;
    private Thread receiveThread;

    // Datos recibidos
    private Vector3 cursorTargetPos;
    private string currentGesture = "none";
    private bool glueCommand = false;
    private bool rotateCommand = false;
    private Vector3 targetRotationEuler = Vector3.zero;
    private float currentScale = 1.0f;

    [Header("Referencias")]
    public Transform shadowCursor;

    [Header("Configuración movimiento")]
    public float moveSpeed = 15.0f;

    // Límites de movimiento del cursor
    public Vector2 xLimit = new Vector2(-10, 10);
    public Vector2 yLimit = new Vector2(0.2f, 10); // solo usado en modo 3D normal
    public Vector2 zLimit = new Vector2(-10, 20);

    [Header("Vista Top-Down")]
    [Tooltip("Si está activo, se usa vista cenital: X,Z con altura fija Y.")]
    public bool topDownView = true;
    [Tooltip("Altura fija del cursor (y) cuando topDownView está activo.")]
    public float cursorHeight = 1.0f;

    [Header("Interacción")]
    public float grabRadius = 1.0f;
    public float glueRadius = 1.5f;

    [Header("Zoom (escala del objeto seleccionado)")]
    [Tooltip("Habilita el zoom controlado desde Python (campo 'scale').")]
    public bool enableZoom = true;
    public float minScale = 0.4f;
    public float maxScale = 1.5f;

    [Header("Plano de límite")]
    [Tooltip("Plano que define el área donde puede moverse el objeto (GroundPlane).")]
    public Transform groundPlane;
    private float planeMinX, planeMaxX;
    private float planeMinZ, planeMaxZ;

    // Estado de objetos seleccionados
    private Transform selectedObject;
    private Transform attachedObject;
    private Rigidbody selectedRb;
    private Rigidbody attachedRb;
    private Vector3 grabOffset;

    [Serializable]
    public class HandData
    {
        public float hand_x;
        public float hand_y;
        public float hand_z;

        public string gesture;
        public bool glue;
        public bool rotate;

        public float rot_x;
        public float rot_y;

        public bool snap;
        public float scale;
    }

    // ----------------------------------------------------------------
    // Ciclo de vida
    // ----------------------------------------------------------------
    void Start()
    {
        try
        {
            receiveThread = new Thread(new ThreadStart(ReceiveData));
            receiveThread.IsBackground = true;
            receiveThread.Start();
        }
        catch (Exception e)
        {
            Debug.LogError("[HandManager] Error iniciando hilo de recepción: " + e.Message);
        }

        // Calcular límites del plano si está asignado
        UpdatePlaneLimits();
    }

    void OnApplicationQuit()
    {
        try
        {
            if (client != null)
            {
                client.Close();
                client = null;
            }
        }
        catch (Exception) { }

        try
        {
            if (receiveThread != null && receiveThread.IsAlive)
            {
                receiveThread.Abort();
                receiveThread = null;
            }
        }
        catch (Exception) { }
    }

    // ----------------------------------------------------------------
    // Calcular límites del GroundPlane
    // ----------------------------------------------------------------
    private void UpdatePlaneLimits()
    {
        if (groundPlane == null) return;

        // Un Plane de Unity mide 10x10 unidades cuando su escala es (1,1,1).
        float halfSizeX = 5f * groundPlane.localScale.x;
        float halfSizeZ = 5f * groundPlane.localScale.z;

        planeMinX = groundPlane.position.x - halfSizeX;
        planeMaxX = groundPlane.position.x + halfSizeX;
        planeMinZ = groundPlane.position.z - halfSizeZ;
        planeMaxZ = groundPlane.position.z + halfSizeZ;

        // También actualizamos los límites del cursor
        xLimit = new Vector2(planeMinX, planeMaxX);
        zLimit = new Vector2(planeMinZ, planeMaxZ);
    }

    // ----------------------------------------------------------------
    // Recepción de datos desde Python
    // ----------------------------------------------------------------
    private void ReceiveData()
    {
        client = new UdpClient(port);
        Debug.Log("[HandManager] Escuchando UDP en puerto " + port);

        while (true)
        {
            try
            {
                IPEndPoint anyIP = new IPEndPoint(IPAddress.Any, 0);
                byte[] data = client.Receive(ref anyIP);
                string text = Encoding.UTF8.GetString(data);

                HandData info = JsonUtility.FromJson<HandData>(text);
                if (info == null)
                    continue;

                // ------------------------------
                // Mapeo de posición del cursor
                // ------------------------------
                if (topDownView)
                {
                    // Vista desde arriba:
                    // hand_x -> X, hand_y -> Z (plano)
                    float x = Mathf.Lerp(xLimit.x, xLimit.y, Mathf.Clamp01(info.hand_x));
                    float z = Mathf.Lerp(zLimit.x, zLimit.y, Mathf.Clamp01(info.hand_y));
                    cursorTargetPos = new Vector3(x, cursorHeight, z);
                }
                else
                {
                    // Modo 3D completo
                    float x = Mathf.Lerp(xLimit.x, xLimit.y, Mathf.Clamp01(info.hand_x));
                    float y = Mathf.Lerp(yLimit.x, yLimit.y, Mathf.Clamp01(info.hand_y));
                    float z = Mathf.Lerp(zLimit.x, zLimit.y, Mathf.Clamp01(info.hand_z));
                    cursorTargetPos = new Vector3(x, y, z);
                }

                // Gestos & comandos
                currentGesture = string.IsNullOrEmpty(info.gesture) ? "none" : info.gesture;
                glueCommand = info.glue;
                rotateCommand = info.rotate;
                targetRotationEuler = new Vector3(info.rot_x, info.rot_y, 0f);

                // Zoom (escala)
                if (enableZoom)
                {
                    float s = info.scale;
                    if (float.IsNaN(s) || float.IsInfinity(s))
                        s = 1.0f;

                    currentScale = Mathf.Clamp(s, minScale, maxScale);
                }
            }
            catch (Exception)
            {
                // Silencioso para no llenar consola si hay paquetes raros
            }
        }
    }

    // ----------------------------------------------------------------
    // Lógica principal en Unity
    // ----------------------------------------------------------------
    void Update()
    {
        // (Opcional) si el plano se ajusta dinámicamente, recalcular límites
        if (groundPlane != null)
        {
            UpdatePlaneLimits();
        }

        // Mover el cursor hacia la posición objetivo
        transform.position = Vector3.Lerp(
            transform.position,
            cursorTargetPos,
            Time.deltaTime * moveSpeed
        );

        // Sombra proyectada al suelo
        if (shadowCursor != null)
        {
            RaycastHit hit;
            if (Physics.Raycast(transform.position, Vector3.down, out hit, 50.0f))
            {
                shadowCursor.position = hit.point + Vector3.up * 0.02f;
                shadowCursor.gameObject.SetActive(true);
            }
            else
            {
                shadowCursor.gameObject.SetActive(false);
            }
        }

        // Colores del cursor según estado
        Renderer rend = GetComponent<Renderer>();
        if (rend != null)
        {
            if (glueCommand) rend.material.color = Color.magenta;
            else if (currentGesture == "move") rend.material.color = Color.red;
            else if (rotateCommand) rend.material.color = Color.blue;
            else rend.material.color = Color.green;
        }

        // ------------------------------
        // Lógica de interacción principal
        // ------------------------------
        if (currentGesture == "move")
        {
            // Intentar seleccionar/AGARRAR un objeto si aún no hay uno
            if (selectedObject == null)
                TryToGrab();

            if (selectedObject != null)
            {
                // Seguir el cursor con offset
                Vector3 targetPos = transform.position + grabOffset;

                // Limitar al área del plano usando el tamaño del objeto
                if (groundPlane != null)
                {
                    Renderer objRend = selectedObject.GetComponent<Renderer>();
                    if (objRend != null)
                    {
                        Vector3 ext = objRend.bounds.extents; // mitad del tamaño en mundo

                        float minX = planeMinX + ext.x;
                        float maxX = planeMaxX - ext.x;
                        float minZ = planeMinZ + ext.z;
                        float maxZ = planeMaxZ - ext.z;

                        targetPos.x = Mathf.Clamp(targetPos.x, minX, maxX);
                        targetPos.z = Mathf.Clamp(targetPos.z, minZ, maxZ);
                    }
                    else
                    {
                        targetPos.x = Mathf.Clamp(targetPos.x, planeMinX, planeMaxX);
                        targetPos.z = Mathf.Clamp(targetPos.z, planeMinZ, planeMaxZ);
                    }
                }

                // Aplicar la posición
                selectedObject.position = Vector3.Lerp(
                    selectedObject.position,
                    targetPos,
                    Time.deltaTime * 20f
                );

                // Si estamos en modo top-down y rotando, mantenerlo suspendido
                if (topDownView && rotateCommand)
                {
                    Vector3 p = selectedObject.position;
                    p.y = cursorHeight;
                    selectedObject.position = p;
                }

                // Rotación controlada desde Python
                if (rotateCommand)
                {
                    Quaternion targetRot = Quaternion.Euler(targetRotationEuler);
                    selectedObject.rotation = Quaternion.Lerp(
                        selectedObject.rotation,
                        targetRot,
                        Time.deltaTime * 10f
                    );
                }

                // Unir objeto vecino (glue)
                if (glueCommand)
                {
                    if (attachedObject == null)
                        TryToAttachNeighbor();
                }
                else
                {
                    if (attachedObject != null)
                        ReleaseAttached();
                }

                // Aplicar zoom (escala) al objeto seleccionado
                if (enableZoom)
                {
                    Vector3 targetScale = Vector3.one * currentScale;
                    selectedObject.localScale = Vector3.Lerp(
                        selectedObject.localScale,
                        targetScale,
                        Time.deltaTime * 10f
                    );
                }
            }
        }
        else
        {
            // Si dejamos de estar en gesto de movimiento
            if (selectedObject != null)
            {
                if (attachedObject != null)
                    ReleaseAttached();

                ReleaseObject();
            }
        }
    }

    // ----------------------------------------------------------------
    // Selección de objeto cercano
    // ----------------------------------------------------------------
    void TryToGrab()
    {
        Collider[] hits = Physics.OverlapSphere(transform.position, grabRadius);
        float closestDist = Mathf.Infinity;
        Transform closestObj = null;

        foreach (var hit in hits)
        {
            if (hit.CompareTag("Interactable"))
            {
                float d = Vector3.Distance(transform.position, hit.transform.position);
                if (d < closestDist)
                {
                    closestDist = d;
                    closestObj = hit.transform;
                }
            }
        }

        if (closestObj != null)
        {
            selectedObject = closestObj;
            selectedRb = selectedObject.GetComponent<Rigidbody>();

            if (selectedRb != null)
            {
                selectedRb.useGravity = false;
                selectedRb.isKinematic = true;
            }

            grabOffset = selectedObject.position - transform.position;
        }
    }

    // ----------------------------------------------------------------
    // Pegado (glue) de un vecino
    // ----------------------------------------------------------------
    void TryToAttachNeighbor()
    {
        if (selectedObject == null)
            return;

        Collider[] hits = Physics.OverlapSphere(selectedObject.position, glueRadius);
        float closestDist = Mathf.Infinity;
        Transform neighbor = null;

        foreach (var hit in hits)
        {
            if (hit.CompareTag("Interactable") && hit.transform != selectedObject)
            {
                float d = Vector3.Distance(selectedObject.position, hit.transform.position);
                if (d < closestDist)
                {
                    closestDist = d;
                    neighbor = hit.transform;
                }
            }
        }

        if (neighbor != null)
        {
            attachedObject = neighbor;
            attachedRb = attachedObject.GetComponent<Rigidbody>();

            if (attachedRb != null)
            {
                attachedRb.useGravity = false;
                attachedRb.isKinematic = true;
            }

            attachedObject.SetParent(selectedObject);
        }
    }

    void ReleaseAttached()
    {
        if (attachedObject != null)
        {
            attachedObject.SetParent(null);

            if (attachedRb != null)
            {
                attachedRb.useGravity = true;
                attachedRb.isKinematic = false;
            }

            attachedObject = null;
            attachedRb = null;
        }
    }

    void ReleaseObject()
    {
        if (selectedObject == null)
            return;

        if (selectedRb != null)
        {
            selectedRb.useGravity = true;
            selectedRb.isKinematic = false;
        }

        selectedObject = null;
        selectedRb = null;
        grabOffset = Vector3.zero;
    }

    // ----------------------------------------------------------------
    // Debug visual en la escena
    // ----------------------------------------------------------------
    void OnDrawGizmosSelected()
    {
        // Radio de agarre alrededor del cursor
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, grabRadius);

        // Si hay objeto seleccionado, dibujar radio de glue
        if (selectedObject != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(selectedObject.position, glueRadius);
        }

        // Dibujar bordes del plano si está definido
        if (groundPlane != null)
        {
            Gizmos.color = Color.white;
            Vector3 p1 = new Vector3(planeMinX, groundPlane.position.y, planeMinZ);
            Vector3 p2 = new Vector3(planeMaxX, groundPlane.position.y, planeMinZ);
            Vector3 p3 = new Vector3(planeMaxX, groundPlane.position.y, planeMaxZ);
            Vector3 p4 = new Vector3(planeMinX, groundPlane.position.y, planeMaxZ);
            Gizmos.DrawLine(p1, p2);
            Gizmos.DrawLine(p2, p3);
            Gizmos.DrawLine(p3, p4);
            Gizmos.DrawLine(p4, p1);
        }
    }
}
