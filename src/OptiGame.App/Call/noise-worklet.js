// Suppression du bruit de fond de la voix (page d'appel, Call/call.html) : filtre RNNoise (réseau de neurones spécialisé dans la
// voix, Xiph.Org / Mozilla, licence BSD) compilé en WebAssembly (rnnoise-sync.js, paquet @jitsi/rnnoise-wasm 0.2.1, empreinte
// vérifiée ; licences dans installer/THIRD-PARTY-NOTICES.txt). Tourne dans le fil audio du moteur web, sur ce PC : rien n'est
// envoyé ailleurs. RNNoise traite des trames de 480 échantillons (10 ms à 48 kHz) ; le moteur donne des blocs de 128 : file
// d'entrée et de sortie, 10 ms de retard ajoutés.
import createRNNWasmModuleSync from './rnnoise-sync.js';

const FRAME = 480;
const SCALE = 32768; // RNNoise attend des échantillons à l'échelle 16 bits

class NoiseSuppressor extends AudioWorkletProcessor {
  constructor() {
    super();
    this.rnn = createRNNWasmModuleSync();
    this.state = this.rnn._rnnoise_create();
    this.framePtr = this.rnn._malloc(FRAME * 4);
    this.input = new Float32Array(FRAME);
    this.inputLength = 0;
    this.output = new Float32Array(FRAME * 4);
    this.outputRead = 0;
    this.outputWrite = FRAME; // 10 ms de silence au départ : de quoi toujours avoir un bloc prêt
    this.enabled = true;
    this.frames = 0;
    this.vad = 0; // probabilité de voix (0 à 1) cumulée, donnée par RNNoise
    this.port.onmessage = e => { if (typeof e.data.enabled === 'boolean') this.enabled = e.data.enabled; };
  }

  process(inputs, outputs) {
    const input = inputs[0] && inputs[0][0];
    const output = outputs[0] && outputs[0][0];
    if (!output) return true;
    if (!input) { output.fill(0); return true; }
    if (!this.enabled) { output.set(input); return true; }

    for (let i = 0; i < input.length; i++) {
      this.input[this.inputLength++] = input[i] * SCALE;
      if (this.inputLength === FRAME) {
        const heap = this.rnn.HEAPF32; // relu à chaque trame : la mémoire du module peut grandir
        heap.set(this.input, this.framePtr >> 2);
        this.vad += this.rnn._rnnoise_process_frame(this.state, this.framePtr, this.framePtr);
        if (++this.frames % 100 === 0) { this.port.postMessage({ frames: this.frames, vad: this.vad / 100 }); this.vad = 0; } // chaque seconde
        const cleaned = heap.subarray(this.framePtr >> 2, (this.framePtr >> 2) + FRAME);
        for (let j = 0; j < FRAME; j++) {
          this.output[this.outputWrite] = cleaned[j] / SCALE;
          this.outputWrite = (this.outputWrite + 1) % this.output.length;
        }
        this.inputLength = 0;
      }
    }
    for (let i = 0; i < output.length; i++) {
      if (this.outputRead === this.outputWrite) { output[i] = 0; continue; }
      output[i] = this.output[this.outputRead];
      this.outputRead = (this.outputRead + 1) % this.output.length;
    }
    return true;
  }
}

registerProcessor('noise-suppressor', NoiseSuppressor);
