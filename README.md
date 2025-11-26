# Modelado3D - Sistema de Control de Manos con Unity

Sistema de modelado 3D controlado por gestos de manos usando MediaPipe y Unity.

## Estructura del Proyecto

- `HandTrackingClient/` - Proyecto Unity que recibe datos de las manos vía UDP
- `PythonController/` - Script Python que detecta manos con la cámara web y envía datos

## Requisitos

### Python
- Python 3.8+
- Dependencias: `pip install -r PythonController/requirements.txt`

### Unity
- Unity 2021.3 o superior
- Universal Render Pipeline (URP)

## Configuración

1. **Instalar dependencias Python:**
   ```bash
   cd PythonController
   python -m venv venv
   venv\Scripts\activate  # En Windows
   pip install -r requirements.txt
   ```

2. **Abrir el proyecto Unity:**
   - Abre `HandTrackingClient/` en Unity Hub
   - Asegúrate de que la escena `SampleScene` esté abierta

3. **Configurar objetos en Unity:**
   - Todos los objetos interactuables deben tener el tag `Interactable`
   - Deben tener un `Collider` y `Rigidbody`

## Uso

1. **Iniciar el controlador Python:**
   ```bash
   cd PythonController
   venv\Scripts\activate
   python main.py
   ```

2. **Ejecutar la escena en Unity** (Play mode)

## Controles de Manos

### Mano Izquierda (Verde)
- **Posición:** Controla X (laterales) y Y (altura) del cursor
- **Puño (0 dedos):** Modo rotación - mueve la mano para rotar objetos
- **Índice + Medio (2 dedos):** Pegar objetos cercanos

### Mano Derecha (Azul)
- **Tamaño de mano:** Controla Z (profundidad) - acerca/aleja objetos
- **Pellizco (pulgar + índice):** Agarrar/mover objetos

## Configuración de Red

Por defecto usa:
- IP: `127.0.0.1` (localhost)
- Puerto: `5052` (UDP)

Puedes modificar estos valores en:
- Python: Variables `UDP_IP` y `UDP_PORT` en `main.py`
- Unity: Campo `port` en el componente `HandManager`
