using UnityEngine;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System;

public class HandManager : MonoBehaviour
{
    public int port = 5052;
    private UdpClient client;
    private Thread receiveThread;

    private Vector3 cursorTargetPos;
    private string currentGesture = "none";
    private bool glueCommand = false;
    private bool rotateCommand = false;
    private Vector3 targetRotationEuler = Vector3.zero;

    [Header("Referencias")]
    public Transform shadowCursor;

    [Header("Configuración")]
    public float moveSpeed = 15.0f;
    
    // Límites para que el cursor no se escape
    public Vector2 xLimit = new Vector2(-10, 10);
    public Vector2 yLimit = new Vector2(0.2f, 10); // Y es altura (0.2 = suelo)
    public Vector2 zLimit = new Vector2(-10, 20);  // Z es profundidad

    [Header("Interacción")]
    public float grabRadius = 1.0f;
    public float glueRadius = 1.5f;

    private Transform selectedObject; 
    private Transform attachedObject; 
    private Rigidbody selectedRb; 
    private Rigidbody attachedRb;
    private Vector3 grabOffset;       

    [Serializable]
    public class HandData
    {
        public float hand_x; public float hand_y; public float hand_z;
        public string gesture; public bool glue; public bool rotate;
        public float rot_x; public float rot_y; public bool snap; public float scale; 
    }

    void Start()
    {
        try {
            receiveThread = new Thread(new ThreadStart(ReceiveData));
            receiveThread.IsBackground = true;
            receiveThread.Start();
        } catch(Exception) {}
    }

    private void ReceiveData()
    {
        client = new UdpClient(port);
        while (true)
        {
            try
            {
                IPEndPoint anyIP = new IPEndPoint(IPAddress.Any, 0);
                byte[] data = client.Receive(ref anyIP);
                string text = Encoding.UTF8.GetString(data);
                HandData info = JsonUtility.FromJson<HandData>(text);

                // MAPEO DIRECTO (Python ya calculó todo)
                // X = Lados (Mano Izq)
                // Y = Altura (Mano Izq)
                // Z = Profundidad (Mano Der)
                
                float x = Mathf.Clamp(info.hand_x, xLimit.x, xLimit.y);
                float y = Mathf.Clamp(info.hand_y, yLimit.x, yLimit.y);
                float z = Mathf.Clamp(info.hand_z, zLimit.x, zLimit.y);

                cursorTargetPos = new Vector3(x, y, z);
                
                currentGesture = info.gesture;
                glueCommand = info.glue;
                rotateCommand = info.rotate;
                targetRotationEuler = new Vector3(info.rot_x, info.rot_y, 0);
            }
            catch (Exception) { }
        }
    }

    void Update()
    {
        // Mover Cursor
        transform.position = Vector3.Lerp(transform.position, cursorTargetPos, Time.deltaTime * moveSpeed);
        
        // Sombra
        if (shadowCursor != null) {
            RaycastHit hit;
            if (Physics.Raycast(transform.position, Vector3.down, out hit, 50.0f)) {
                shadowCursor.position = hit.point + Vector3.up * 0.02f;
                shadowCursor.gameObject.SetActive(true);
            } else shadowCursor.gameObject.SetActive(false);
        }

        // Colores
        Renderer rend = GetComponent<Renderer>();
        if(rend != null) {
            if (glueCommand) rend.material.color = Color.magenta;
            else if (currentGesture == "move") rend.material.color = Color.red;
            else if (rotateCommand) rend.material.color = Color.blue;
            else rend.material.color = Color.green;
        }

        // Lógica Principal
        if (currentGesture == "move")
        {
            if (selectedObject == null) TryToGrab();
            
            if (selectedObject != null)
            {
                selectedObject.position = Vector3.Lerp(selectedObject.position, transform.position + grabOffset, Time.deltaTime * 20);
                
                if (rotateCommand)
                    selectedObject.rotation = Quaternion.Lerp(selectedObject.rotation, Quaternion.Euler(targetRotationEuler), Time.deltaTime * 10);

                if (glueCommand)
                {
                    if (attachedObject == null) TryToAttachNeighbor();
                }
                else
                {
                    if (attachedObject != null) ReleaseAttached();
                }
            }
        }
        else 
        {
            if (selectedObject != null)
            {
                if (attachedObject != null) ReleaseAttached();
                ReleaseObject();
            }
        }
    }

    void TryToGrab()
    {
        Collider[] hits = Physics.OverlapSphere(transform.position, grabRadius);
        float closestDist = Mathf.Infinity;
        Transform closestObj = null;
        foreach (var hit in hits) {
            if (hit.CompareTag("Interactable")) {
                float d = Vector3.Distance(transform.position, hit.transform.position);
                if (d < closestDist) { closestDist = d; closestObj = hit.transform; }
            }
        }
        if (closestObj != null) {
            selectedObject = closestObj;
            selectedRb = selectedObject.GetComponent<Rigidbody>();
            if (selectedRb != null) { selectedRb.useGravity = false; selectedRb.isKinematic = true; }
            grabOffset = selectedObject.position - transform.position;
        }
    }

    void TryToAttachNeighbor()
    {
        Collider[] hits = Physics.OverlapSphere(selectedObject.position, glueRadius);
        float closestDist = Mathf.Infinity;
        Transform neighbor = null;
        foreach (var hit in hits) {
            if (hit.CompareTag("Interactable") && hit.transform != selectedObject) {
                float d = Vector3.Distance(selectedObject.position, hit.transform.position);
                if (d < closestDist) { closestDist = d; neighbor = hit.transform; }
            }
        }
        if (neighbor != null) {
            attachedObject = neighbor;
            attachedRb = attachedObject.GetComponent<Rigidbody>();
            if (attachedRb != null) { attachedRb.useGravity = false; attachedRb.isKinematic = true; }
            attachedObject.SetParent(selectedObject);
        }
    }

    void ReleaseAttached()
    {
        if (attachedObject != null) {
            attachedObject.SetParent(null);
            if (attachedRb != null) { attachedRb.useGravity = true; attachedRb.isKinematic = false; }
            attachedObject = null; attachedRb = null;
        }
    }

    void ReleaseObject()
    {
        if (selectedRb != null) { selectedRb.useGravity = true; selectedRb.isKinematic = false; }
        selectedObject = null; selectedRb = null;
    }
    
    void OnDrawGizmos() { 
        Gizmos.color = Color.yellow; Gizmos.DrawWireSphere(transform.position, grabRadius); 
    }
    void OnApplicationQuit() { if (receiveThread != null) receiveThread.Abort(); if (client != null) client.Close(); }
}