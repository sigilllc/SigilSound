// Plays interleaved stereo float32 chunks posted from the page, through a ring buffer.
// When the page falls behind, it outputs silence and counts an underrun.
const FADE_FRAMES = 256;

class SigilOutput extends AudioWorkletProcessor {
    constructor() {
        super();
        this.capacity = sampleRate * 10;            // frames
        this.ring = new Float32Array(this.capacity * 2);
        this.writeIndex = 0;                        // frames, monotonic
        this.readIndex = 0;
        this.underruns = 0;
        this.quanta = 0;
        this.fadeIn = 0;
        this.port.onmessage = e => (e.data.flush !== undefined ? this.flush(e.data.flush) : this.write(e.data));
    }

    // Drops everything but the next `keep` frames, so an edit is heard right away instead of after the queued audio.
    // The kept audio fades out and the audio that follows fades in, so the cut doesn't click.
    flush(keep) {
        const target = Math.min(this.writeIndex, this.readIndex + keep);
        const fade = Math.min(FADE_FRAMES, target - this.readIndex);
        for (let i = 0; i < fade; i++) {
            const at = ((target - fade + i) % this.capacity) * 2;
            const gain = 1 - (i + 1) / fade;
            this.ring[at] *= gain;
            this.ring[at + 1] *= gain;
        }
        this.writeIndex = target;
        this.fadeIn = FADE_FRAMES;
    }

    write(samples) {
        const frames = samples.length / 2;
        for (let i = 0; i < frames; i++) {
            const at = ((this.writeIndex + i) % this.capacity) * 2;
            const gain = this.fadeIn > 0 ? 1 - this.fadeIn-- / FADE_FRAMES : 1;
            this.ring[at] = samples[2 * i] * gain;
            this.ring[at + 1] = samples[2 * i + 1] * gain;
        }
        this.writeIndex += frames;
        // Never let the writer lap the reader; drop the oldest audio instead.
        if (this.writeIndex - this.readIndex > this.capacity)
            this.readIndex = this.writeIndex - this.capacity;
    }

    process(_, outputs) {
        const [left, right] = outputs[0];
        let missing = false;
        for (let i = 0; i < left.length; i++) {
            if (this.readIndex < this.writeIndex) {
                const at = (this.readIndex % this.capacity) * 2;
                left[i] = this.ring[at];
                right[i] = this.ring[at + 1];
                this.readIndex++;
            } else {
                left[i] = right[i] = 0;
                missing = true;
            }
        }
        if (missing && this.writeIndex > 0) this.underruns++;

        if (++this.quanta % 4 === 0)
            this.port.postMessage({ read: this.readIndex, underruns: this.underruns });
        return true;
    }
}

registerProcessor("sigil-output", SigilOutput);
