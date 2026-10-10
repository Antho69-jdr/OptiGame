// Traitement de la voix avant l'envoi (page d'appel, Call/call.html), dans le fil audio du moteur web, sur ce PC (rien n'est
// envoyé ailleurs). Trames de 480 échantillons (10 ms à 48 kHz) ; le moteur donne des blocs de 128 : files d'entrée et de
// sortie, 10 ms de retard ajoutés.
// 1. Suppression du bruit : filtre RNNoise (réseau de neurones spécialisé dans la voix, Xiph.Org / Mozilla, licence BSD) compilé
//    en WebAssembly (rnnoise-sync.js, paquet @jitsi/rnnoise-wasm 0.2.1 tel que publié ; licences dans
//    installer/THIRD-PARTY-NOTICES.txt). Il retire les bruits, pas les autres voix.
//    « Maximale » : filtre DeepFilterNet 3 (réseau plus grand, MIT / Apache-2.0) compilé en WebAssembly par la CI depuis le code
//    officiel (dfn/, .github/workflows/deepfilter-wasm.yml) ; octets du moteur et du modèle transmis par la page, moteur compilé
//    ICI sans bloquer le son (un WebAssembly.Module compilé par la page n'arrive pas : « messageerror » silencieux, constaté le
//    2026-10-10), filtre créé à la trame suivante ; RNNoise en attendant. Coût mesuré par les vérifications (temps processeur
//    des processus du moteur web : l'horloge du fil audio, à la milliseconde, est trop grossière).
// 2. Seuil du micro (« noise gate ») : le son ne passe qu'au-dessus d'un niveau ; une voix en fond, plus faible que la vôtre au
//    micro, est coupée. Automatique : seuil placé entre le fond (minimum suivi lentement) et votre voix (niveau des trames où
//    RNNoise reconnaît de la voix) ; manuel : seuil choisi en dBFS. Ouverture immédiate, maintien 250 ms, fermeture en 60 ms.
import createRNNWasmModuleSync from './rnnoise-sync.js';
import { initSync as initDfn, df_create, df_get_frame_length, df_process_frame } from './dfn/df.js';

const FRAME = 480;
const SCALE = 32768; // RNNoise attend des échantillons à l'échelle 16 bits
const HOLD_FRAMES = 25; // 250 ms
const RELEASE = 1 / (0.06 * 48000); // fermeture en 60 ms, par échantillon
const ATTACK = 1 / (0.005 * 48000); // ouverture en 5 ms, par échantillon
const REPORT_FRAMES = 10; // mesures envoyées 10 fois par seconde

class VoiceProcessor extends AudioWorkletProcessor {
  constructor() {
    super();
    this.rnn = null; // ≈ 70 Mo : créé seulement quand la suppression du bruit sert
    this.input = new Float32Array(FRAME);
    this.inputLength = 0;
    this.output = new Float32Array(FRAME * 4);
    this.outputRead = 0;
    this.outputWrite = FRAME; // 10 ms de silence au départ : de quoi toujours avoir un bloc prêt
    this.denoise = true;
    this.engine = 'rnnoise'; // ou 'dfn' (« Maximale »)
    this.dfnModule = null; // WebAssembly.Module, compilé ici à partir des octets envoyés par la page
    this.dfnModel = null; // modèle (DeepFilterNet3_onnx.tar.gz), libéré une fois le filtre créé
    this.dfn = 0; // état du filtre (pointeur dans la mémoire du module), 0 = pas encore créé
    this.dfnFailed = false;
    this.dfnFrame = new Float32Array(FRAME);

    this.gateMode = 'off';
    this.manualThreshold = -30;
    this.floor = -60; // fond sonore (dBFS)
    this.voice = -25; // niveau de votre voix (dBFS)
    this.hold = 0;
    this.gain = 1;
    this.frames = 0;
    this.peak = -100;
    this.vadSum = 0;
    this.openFrames = 0;
    this.port.onmessage = e => {
      const m = e.data;
      if (typeof m.denoise === 'boolean') this.denoise = m.denoise;
      if (typeof m.gateMode === 'string') this.gateMode = m.gateMode;
      if (typeof m.threshold === 'number') this.manualThreshold = m.threshold;
      if (typeof m.engine === 'string') this.engine = m.engine;
      if (m.dfn) {
        this.dfnModel = m.dfn.model;
        const started = Date.now();
        WebAssembly.compile(m.dfn.wasm).then(module => {
          this.port.postMessage({ maxStep: 'moteur compilé dans le fil audio en ' + (Date.now() - started) + ' ms' });
          this.dfnModule = module;
        }, err => {
          this.dfnFailed = true;
          this.port.postMessage({ maxFailed: String((err && err.message) || err) });
        });
      }
    };
  }

  threshold() {
    if (this.gateMode === 'manual') return this.manualThreshold;
    // Entre le fond et la voix, 6 à 10 dB sous la voix (les voix de fond arrivent 10 à 20 dB plus bas au micro ; resserré le 2026-10-10 : le seuil manuel à −30 dB
    // marchait mieux que l'ancien automatique, 6 à 15 dB sous la voix), borné.
    const between = this.voice - Math.min(10, Math.max(6, (this.voice - this.floor) * 0.3));
    return Math.min(-20, Math.max(this.floor + 6, between));
  }

  // Filtre « Maximale » : créé une fois (≈ 1 s de calcul, le son envoyé est coupé pendant ce temps), faux s'il n'est pas utilisable.
  ensureDfn() {
    if (this.dfn || this.dfnFailed || !this.dfnModule || !this.dfnModel) return !!this.dfn;
    const started = Date.now();
    this.port.postMessage({ maxStep: 'création du filtre dans le fil audio' });
    try {
      initDfn(this.dfnModule);
      this.port.postMessage({ maxStep: 'moteur instancié en ' + (Date.now() - started) + ' ms' });
      this.dfn = df_create(new Uint8Array(this.dfnModel), 100);
      const length = df_get_frame_length(this.dfn);
      if (length !== FRAME) throw new Error('trames de ' + length + ' échantillons au lieu de ' + FRAME);
      this.port.postMessage({ maxReady: Date.now() - started });
    } catch (err) {
      this.dfn = 0;
      this.dfnFailed = true;
      this.port.postMessage({ maxFailed: String((err && err.message) || err) });
    }
    this.dfnModel = null;
    return !!this.dfn;
  }

  frame() {
    let vad = 1;
    if (this.denoise && this.engine === 'dfn' && this.ensureDfn()) {
      for (let j = 0; j < FRAME; j++) this.dfnFrame[j] = this.input[j] / SCALE;
      const out = df_process_frame(this.dfn, this.dfnFrame);
      for (let j = 0; j < FRAME; j++) this.input[j] = out[j] * SCALE;
    } else if (this.denoise) {
      if (!this.rnn) {
        this.rnn = createRNNWasmModuleSync();
        this.state = this.rnn._rnnoise_create();
        this.framePtr = this.rnn._malloc(FRAME * 4);
      }
      const heap = this.rnn.HEAPF32; // relu à chaque trame : la mémoire du module peut grandir
      heap.set(this.input, this.framePtr >> 2);
      vad = this.rnn._rnnoise_process_frame(this.state, this.framePtr, this.framePtr);
      this.input.set(heap.subarray(this.framePtr >> 2, (this.framePtr >> 2) + FRAME));
    }
    let energy = 0;
    for (let j = 0; j < FRAME; j++) { const x = this.input[j] / SCALE; energy += x * x; }
    const db = 10 * Math.log10(energy / FRAME + 1e-12);

    // Apprentissage du seuil automatique : fond = minimum qui remonte lentement (≈ 3 dB/s) ; voix = trames reconnues comme voix.
    this.floor = db < this.floor ? db : this.floor + 0.03;
    if (vad > 0.8 && db > this.floor + 12) this.voice += (db - this.voice) * 0.05;

    let open = true;
    if (this.gateMode !== 'off') {
      const loud = db > this.threshold() && (!this.denoise || vad > 0.3);
      if (loud) this.hold = HOLD_FRAMES; else if (this.hold > 0) this.hold--;
      open = loud || this.hold > 0;
    }
    for (let j = 0; j < FRAME; j++) {
      this.gain = open ? Math.min(1, this.gain + ATTACK) : Math.max(0, this.gain - RELEASE);
      this.output[this.outputWrite] = this.input[j] / SCALE * this.gain;
      this.outputWrite = (this.outputWrite + 1) % this.output.length;
    }

    this.peak = Math.max(this.peak, db);
    this.vadSum += vad;
    if (open) this.openFrames++;
    if (++this.frames % REPORT_FRAMES === 0) {
      this.port.postMessage({
        frames: this.frames, db: this.peak, vad: this.vadSum / REPORT_FRAMES, open: this.openFrames > 0,
        threshold: this.gateMode === 'off' ? null : this.threshold(),
        engine: this.denoise ? (this.engine === 'dfn' && this.dfn ? 'dfn' : 'rnnoise') : 'none',
      });

      this.peak = -100;
      this.vadSum = 0;
      this.openFrames = 0;
    }
  }

  process(inputs, outputs) {
    const input = inputs[0] && inputs[0][0];
    const output = outputs[0] && outputs[0][0];
    if (!output) return true;
    if (!input) { output.fill(0); return true; }
    for (let i = 0; i < input.length; i++) {
      this.input[this.inputLength++] = input[i] * SCALE;
      if (this.inputLength === FRAME) {
        this.frame();
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

registerProcessor('voice-processor', VoiceProcessor);
