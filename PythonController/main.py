import cv2
import mediapipe as mp
import socket
import json
import math

# --- CONFIGURACIÓN ---
UDP_IP = "127.0.0.1"
UDP_PORT = 5052
sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)

mp_hands = mp.solutions.hands
cap = cv2.VideoCapture(0)

print("--- CONTROL HÍBRIDO 3D ---")
print("Mano IZQ: Mueve X (Lados) y Y (Arriba/Abajo) | Puño = Rotar")
print("Mano DER: Mueve Z (Acercar/Alejar) | Pellizco = Agarrar")

def get_dist(p1, p2):
    return math.sqrt((p1.x - p2.x)**2 + (p1.y - p2.y)**2)

def finger_is_up(tip, pip):
    return tip.y < pip.y

# Variables globales de posición
global_x, global_y, global_z = 0.0, 3.0, 0.0 # Empezamos en el centro, un poco arriba

# Suavizado
alpha = 0.6 

# Estado de Rotación
angle_x, angle_y = 0.0, 0.0
rotating = False
last_rot_hand_pos = None

with mp_hands.Hands(max_num_hands=2, min_detection_confidence=0.7) as hands:
    while cap.isOpened():
        success, image = cap.read()
        if not success: continue

        image = cv2.flip(image, 1)
        h, w, _ = image.shape
        image_rgb = cv2.cvtColor(image, cv2.COLOR_BGR2RGB)
        results = hands.process(image_rgb)
        
        # Variables para este frame
        gesture_right = "none"
        rotate_cmd = False
        glue_cmd = False
        
        if results.multi_hand_landmarks:
            for idx, handedness in enumerate(results.multi_handedness):
                label = handedness.classification[0].label 
                lm = results.multi_hand_landmarks[idx].landmark
                wrist = lm[0]

                # ---------------------------------------------------------
                # MANO IZQUIERDA (Controla X y Y)
                # ---------------------------------------------------------
                if label == 'Left':
                    # 1. POSICIÓN X / Y
                    # Multiplicamos por 20 para dar rango amplio en Unity
                    raw_x = (wrist.x - 0.5) * 25 
                    # Invertimos Y para que "Mano Arriba" sea "Cursor Arriba" (Positivo)
                    # Nota: En Unity Y es altura. 
                    raw_y = (0.5 - wrist.y) * 15 + 2.0 # +2 offset de altura base
                    
                    global_x = global_x * (1 - alpha) + raw_x * alpha
                    global_y = global_y * (1 - alpha) + raw_y * alpha

                    # Visual: Círculo Verde (Mando de Dirección)
                    cx, cy = int(wrist.x * w), int(wrist.y * h)
                    cv2.circle(image, (cx, cy), 15, (0, 255, 0), -1) 
                    
                    # 2. GESTOS (Rotar / Pegar)
                    tips = [lm[8], lm[12], lm[16], lm[20]]
                    pips = [lm[6], lm[10], lm[14], lm[18]]
                    fingers_up = [finger_is_up(tips[i], pips[i]) for i in range(4)]
                    
                    # Puño (0 dedos) -> Rotar
                    if sum(fingers_up) == 0:
                        rotate_cmd = True
                        curr_pos = (wrist.x, wrist.y)
                        if not rotating:
                            rotating = True
                            last_rot_hand_pos = curr_pos
                        else:
                            dx = curr_pos[0] - last_rot_hand_pos[0]
                            dy = curr_pos[1] - last_rot_hand_pos[1]
                            angle_y += dx * 120 
                            angle_x -= dy * 120
                            last_rot_hand_pos = curr_pos
                        cv2.putText(image, "ROTAR", (cx, cy-30), cv2.FONT_HERSHEY_SIMPLEX, 0.6, (0,0,255), 2)
                    
                    # Índice y Medio (2 dedos) -> Pegar
                    elif fingers_up[0] and fingers_up[1] and not fingers_up[2] and not fingers_up[3]:
                        glue_cmd = True
                        rotating = False
                        cv2.putText(image, "PEGAR", (cx, cy-30), cv2.FONT_HERSHEY_SIMPLEX, 0.6, (255,0,255), 2)
                    else:
                        rotating = False

                # ---------------------------------------------------------
                # MANO DERECHA (Controla Z y Agarre)
                # ---------------------------------------------------------
                if label == 'Right':
                    # 1. POSICIÓN Z (Profundidad basada en TAMAÑO de mano)
                    # Calculamos distancia entre Muñeca(0) y Nudillo Indice(5)
                    # Cerca (Grande) ~ 0.3 | Lejos (Pequeña) ~ 0.05
                    palm_size = get_dist(lm[0], lm[5])
                    
                    # Mapeo: Queremos que 0.2 (Cerca) sea Z=-5 (Hacia ti)
                    # y 0.05 (Lejos) sea Z=15 (Fondo)
                    # Fórmula empírica inversa
                    raw_z = (0.15 - palm_size) * 80.0 
                    
                    global_z = global_z * (1 - alpha) + raw_z * alpha

                    # Visual: Círculo Azul (Mando de Profundidad)
                    cx, cy = int(wrist.x * w), int(wrist.y * h)
                    cv2.circle(image, (cx, cy), 15, (255, 0, 0), -1) 
                    cv2.putText(image, f"Z: {global_z:.1f}", (cx, cy+30), cv2.FONT_HERSHEY_SIMPLEX, 0.5, (255,0,0), 1)

                    # 2. GESTO (Agarrar)
                    thumb = lm[4]
                    index = lm[8]
                    dist = get_dist(thumb, index)
                    if dist < 0.05:
                        gesture_right = "move"
                        cv2.line(image, (int(thumb.x*w), int(thumb.y*h)), (int(index.x*w), int(index.y*h)), (0, 255, 0), 3)

        # Empaquetar datos
        data = {
            "hand_x": global_x, # Izq
            "hand_y": global_y, # Izq
            "hand_z": global_z, # Der (Calculada por tamaño)
            "gesture": gesture_right,
            "glue": glue_cmd,
            "rotate": rotate_cmd,
            "rot_x": angle_x, "rot_y": angle_y,
            "snap": False, "scale": 1.0 
        }

        try:
            sock.sendto(json.dumps(data).encode(), (UDP_IP, UDP_PORT))
        except: pass

        cv2.imshow('Control Hibrido', image)
        if cv2.waitKey(5) & 0xFF == 27: break

cap.release()
cv2.destroyAllWindows()