// The strip that shows the notes a snippet sends out (Out.Notes), as they sound: time runs right to left under a fixed playhead,
// one row for each semitone, like the Output drawer of the Sound Script device in the Sigilgraph Audio Workbench.
//
// It knows nothing about the page. The page gives it note events with the frame of the audio timeline they happen at, and asks
// it to draw for a given frame; the audio is rendered ahead of what is heard, so notes at frames after "now" are drawn to the
// right of the playhead and scroll in as they come due.
(function (global) {
    "use strict";

    const WINDOW_SECONDS = 4;        // what the strip shows
    const PLAYHEAD = 0.72;           // where "now" sits, as a fraction of the width
    const MIN_ROWS = 12;             // an octave, so a single note does not fill the strip
    const BLACK = [1, 3, 6, 8, 10];  // pitch classes of the black keys

    function create() {
        const spans = [];            // { id, start, end, pitch, velocity } in frames; end is Infinity while the note sounds
        const open = new Map();

        return {
            spans,

            // events: [kind, id, pitch, velocity, ...]. Kind 0 starts a note and 1 ends it; id pairs them. `at` is the frame they happen at.
            add(events, at) {
                for (let i = 0; i + 3 < events.length; i += 4) {
                    const kind = events[i], id = events[i + 1];
                    if (kind === 0) {
                        const span = { id, start: at, end: Infinity, pitch: events[i + 2], velocity: events[i + 3] };
                        spans.push(span);
                        open.set(id, span);
                    } else {
                        const span = open.get(id);
                        if (span) { span.end = at; open.delete(id); }
                    }
                }
            },

            // Audio past `at` was thrown away (the sound was replaced): notes that began there never happen, and the rest end there.
            cut(at) {
                for (let i = spans.length - 1; i >= 0; i--) {
                    const span = spans[i];
                    if (span.start >= at) {
                        open.delete(span.id);
                        spans.splice(i, 1);
                    } else if (span.end > at) {
                        span.end = at;
                        open.delete(span.id);
                    }
                }
            },

            // Forgets what has scrolled out of sight.
            prune(before) {
                for (let i = spans.length - 1; i >= 0; i--)
                    if (spans[i].end < before) spans.splice(i, 1);
            },

            clear() {
                spans.length = 0;
                open.clear();
            },
        };
    }

    // Draws the strip for the moment `now` (a frame). `colors` has { back, grid, bar, barSoft, head, text } as CSS colors.
    function draw(canvas, strip, now, sampleRate, colors) {
        const scale = global.devicePixelRatio || 1;
        const w = canvas.clientWidth, h = canvas.clientHeight;
        if (w === 0 || h === 0) return;
        if (canvas.width !== Math.round(w * scale) || canvas.height !== Math.round(h * scale)) {
            canvas.width = Math.round(w * scale);
            canvas.height = Math.round(h * scale);
        }

        const g = canvas.getContext("2d");
        g.setTransform(scale, 0, 0, scale, 0, 0);
        g.clearRect(0, 0, w, h);

        const span = WINDOW_SECONDS * sampleRate;
        const from = now - PLAYHEAD * span;
        const to = from + span;
        const xOf = frame => ((frame - from) / span) * w;

        const visible = strip.spans.filter(s => s.start < to && s.end > from);
        if (now - span > 0) strip.prune(now - span);

        // One row for every semitone from the lowest note to the highest, never fewer than an octave, centred on what is there.
        let low = 60, high = 60;
        if (visible.length > 0) {
            low = Math.min(...visible.map(s => s.pitch));
            high = Math.max(...visible.map(s => s.pitch));
        }
        const rows = Math.max(MIN_ROWS, high - low + 3);
        const bottom = Math.floor((low + high) / 2 - rows / 2 + 0.5);
        const rowH = h / rows;
        const yOf = pitch => h - (pitch - bottom + 1) * rowH;

        for (let r = 0; r < rows; r++) {
            const pitch = bottom + r;
            const y = yOf(pitch);
            if (BLACK.includes(((pitch % 12) + 12) % 12)) {
                g.fillStyle = colors.grid;
                g.fillRect(0, y, w, rowH);
            }
            if (((pitch % 12) + 12) % 12 === 0) {
                g.fillStyle = colors.text;
                g.font = "10px sans-serif";
                g.textBaseline = "middle";
                g.fillText("C" + (Math.floor(pitch / 12) - 1), 4, y + rowH / 2);
            }
        }

        // The beat lines: a second apart, so the scroll has something to move against.
        g.strokeStyle = colors.grid;
        g.lineWidth = 1;
        g.beginPath();
        for (let t = Math.ceil(from / sampleRate); t * sampleRate < to; t++) {
            const x = Math.round(xOf(t * sampleRate)) + 0.5;
            g.moveTo(x, 0);
            g.lineTo(x, h);
        }
        g.stroke();

        for (const s of visible) {
            const x0 = Math.max(0, xOf(s.start));
            const x1 = Math.min(w, Number.isFinite(s.end) ? xOf(s.end) : w);
            const sounding = s.start <= now && s.end > now;
            g.globalAlpha = 0.35 + 0.65 * Math.min(1, Math.max(0, s.velocity));
            g.fillStyle = sounding ? colors.bar : colors.barSoft;
            g.fillRect(x0, yOf(s.pitch) + 1, Math.max(2, x1 - x0), Math.max(2, rowH - 2));
        }
        g.globalAlpha = 1;

        g.strokeStyle = colors.head;
        g.lineWidth = 1.5;
        g.beginPath();
        const px = Math.round(xOf(now)) + 0.5;
        g.moveTo(px, 0);
        g.lineTo(px, h);
        g.stroke();

        if (visible.length === 0) {
            g.fillStyle = colors.text;
            g.font = "12px sans-serif";
            g.textBaseline = "middle";
            g.textAlign = "center";
            g.fillText("No notes yet", w / 2, h / 2);
            g.textAlign = "start";
        }
    }

    global.sigilNoteStrip = { create, draw, WINDOW_SECONDS };
})(window);
