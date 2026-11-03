"""Wake word local do Odin (openWakeWord, roda no PC, nada sai pra internet).

Escuta o microfone padrao e imprime uma linha JSON por deteccao:
    {"evento": "acordou", "modelo": "hey_jarvis", "score": 0.83, "quando": "..."}
O Factory le o stdout e liga a voz do Odin. Sem modelo "odin" pronto ainda: usa hey_jarvis
ate treinar o nosso (ver README.md). --teste N = so mede o mic por N segundos e sai.
"""
import argparse
import json
import sys
import time
from datetime import datetime

import numpy as np
import sounddevice as sd
from openwakeword.model import Model

RATE, BLOCO = 16000, 1280  # 80 ms, o que o openWakeWord espera


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--modelo", default="hey_jarvis", help="nome do modelo pronto ou caminho .onnx")
    ap.add_argument("--limiar", type=float, default=0.5)
    ap.add_argument("--teste", type=float, default=0, help="segundos de teste do mic (0 = escuta pra sempre)")
    a = ap.parse_args()

    oww = Model(wakeword_models=[a.modelo], inference_framework="onnx")
    fim = time.time() + a.teste if a.teste else None
    pico, volume, ultimo = 0.0, 0.0, 0.0
    print(json.dumps({"evento": "pronto", "modelo": a.modelo, "mic": sd.query_devices(kind="input")["name"]}), flush=True)
    with sd.InputStream(samplerate=RATE, channels=1, dtype="int16", blocksize=BLOCO) as mic:
        while fim is None or time.time() < fim:
            bloco, _ = mic.read(BLOCO)
            audio = bloco[:, 0]
            volume = max(volume, float(np.abs(audio).max()))
            for nome, score in oww.predict(audio).items():
                pico = max(pico, float(score))
                if score >= a.limiar and time.time() - ultimo > 2:
                    ultimo = time.time()
                    print(json.dumps({"evento": "acordou", "modelo": nome, "score": round(float(score), 3),
                                      "quando": datetime.now().isoformat(timespec="seconds")}), flush=True)
    print(json.dumps({"evento": "teste_fim", "volume_max": int(volume), "score_max": round(pico, 3)}), flush=True)


if __name__ == "__main__":
    try:
        main()
    except KeyboardInterrupt:
        sys.exit(0)
