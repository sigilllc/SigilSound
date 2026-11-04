// Browser side of the REPL: the Monaco editor and the Web Audio output.
// All C# (compiling, completion, rendering) runs in .NET WebAssembly; this file only moves data.
(() => {
    const MONACO = "https://cdn.jsdelivr.net/npm/monaco-editor@0.52.2/min";

    // Monaco's workers must come from the same origin, so they bootstrap from a data URL.
    window.MonacoEnvironment = {
        getWorkerUrl: () => "data:text/javascript;charset=utf-8," + encodeURIComponent(
            `self.MonacoEnvironment = { baseUrl: "${MONACO}/" }; importScripts("${MONACO}/vs/base/worker/workerMain.js");`)
    };

    let editor;
    let page;
    let checkTimer;

    function loadMonaco() {
        return new Promise((resolve, reject) => {
            require.config({ paths: { vs: `${MONACO}/vs` } });
            require(["vs/editor/editor.main"], () => resolve(window.monaco), reject);
        });
    }

    // Colors from the desktop theme: well background, dim comments, teal only for the cursor and numbers.
    function defineTheme(monaco) {
        monaco.editor.defineTheme("sigil", {
            base: "vs-dark",
            inherit: true,
            rules: [
                { token: "comment", foreground: "a6abb5" },
                { token: "keyword", foreground: "5cc8ff" },
                { token: "number", foreground: "1de9b6" },
                { token: "string", foreground: "ffb000" },
                { token: "identifier", foreground: "eef0f3" },
                { token: "type", foreground: "eef0f3" },
                { token: "delimiter", foreground: "d2d6dd" },
            ],
            colors: {
                "editor.background": "#14151b",
                "editor.foreground": "#eef0f3",
                "editor.lineHighlightBackground": "#1b1c22",
                "editor.lineHighlightBorder": "#00000000",
                "editorLineNumber.foreground": "#5e626c",
                "editorLineNumber.activeForeground": "#a6abb5",
                "editorCursor.foreground": "#1de9b6",
                "editor.selectionBackground": "#1de9b633",
                "editorIndentGuide.background1": "#31333e",
                "editorWidget.background": "#262832",
                "editorWidget.border": "#3b3e4a",
                "editorSuggestWidget.background": "#262832",
                "editorSuggestWidget.border": "#3b3e4a",
                "editorSuggestWidget.selectedBackground": "#30323d",
                "editorSuggestWidget.highlightForeground": "#1de9b6",
                "editorHoverWidget.background": "#262832",
                "editorHoverWidget.border": "#3b3e4a",
                "editorError.foreground": "#ffb000",
                "editorWarning.foreground": "#a6abb5",
                "scrollbarSlider.background": "#30323d80",
            },
        });
    }

    function registerLanguageServices(monaco) {
        monaco.languages.registerCompletionItemProvider("csharp", {
            // A quote opens the list of sample names when the string is the argument of Samples.Load.
            triggerCharacters: [".", "\""],
            async provideCompletionItems(model, position) {
                const items = await page.invokeMethodAsync("Complete", model.getValue(), model.getOffsetAt(position));
                const word = model.getWordUntilPosition(position);
                const wordRange = new monaco.Range(position.lineNumber, word.startColumn, position.lineNumber, word.endColumn);
                return {
                    suggestions: items.map(i => ({
                        label: i.label,
                        // Sample names use the File kind, which the stylesheet draws as a pink waveform.
                        kind: i.kind === "Sample"
                            ? monaco.languages.CompletionItemKind.File
                            : monaco.languages.CompletionItemKind[i.kind] ?? monaco.languages.CompletionItemKind.Text,
                        detail: i.detail,
                        documentation: i.documentation ? { value: i.documentation } : undefined,
                        insertText: i.insertText ?? i.label,
                        filterText: i.filterText ?? undefined,
                        sortText: i.sortText,
                        // A name inside quotes replaces the whole string; the server counts the characters each side of the caret.
                        range: i.replaceBefore != null
                            ? new monaco.Range(position.lineNumber, position.column - i.replaceBefore, position.lineNumber, position.column + i.replaceAfter)
                            : wordRange,
                    })),
                };
            },
        });

        // Documentation for the symbol under the pointer; Ctrl+Click and Ctrl+Q open the same popup.
        monaco.languages.registerHoverProvider("csharp", {
            async provideHover(model, position) {
                const markdown = await page.invokeMethodAsync("Describe", model.getValue(), model.getOffsetAt(position));
                if (!markdown) return null;
                const word = model.getWordAtPosition(position);
                return {
                    range: word ? new monaco.Range(position.lineNumber, word.startColumn, position.lineNumber, word.endColumn) : undefined,
                    contents: [{ value: markdown }],
                };
            },
        });

        monaco.languages.registerSignatureHelpProvider("csharp", {
            signatureHelpTriggerCharacters: ["(", ","],
            signatureHelpRetriggerCharacters: [")"],
            async provideSignatureHelp(model, position) {
                const help = await page.invokeMethodAsync("Signatures", model.getValue(), model.getOffsetAt(position));
                if (!help) return null;
                return {
                    value: {
                        signatures: help.signatures.map(s => ({
                            label: s.label,
                            documentation: s.documentation ? { value: s.documentation } : undefined,
                            parameters: s.parameters.map(p => ({
                                label: [p.start, p.end],
                                documentation: p.documentation ? { value: p.documentation } : undefined,
                            })),
                        })),
                        activeSignature: help.activeSignature,
                        activeParameter: help.activeParameter,
                    },
                    dispose() { },
                };
            },
        });
    }

    function codeFromUrl() {
        const match = location.hash.match(/^#code=([A-Za-z0-9_-]+)$/);
        if (!match) return null;
        try {
            const b64 = match[1].replace(/-/g, "+").replace(/_/g, "/");
            const bytes = Uint8Array.from(atob(b64), c => c.charCodeAt(0));
            return new TextDecoder().decode(bytes);
        } catch {
            return null;
        }
    }

    function scheduleCheck() {
        clearTimeout(checkTimer);
        checkTimer = setTimeout(() => {
            page.invokeMethodAsync("CodeChangedAsync", editor.getValue()).then(setMarkers).catch(() => { });
        }, 700);
    }

    function setMarkers(diagnostics) {
        if (!editor) return;
        const markers = diagnostics.filter(d => d.startLine > 0).map(d => ({
            severity: d.isError ? monaco.MarkerSeverity.Error : monaco.MarkerSeverity.Warning,
            message: d.message,
            code: d.id,
            startLineNumber: d.startLine,
            startColumn: d.startColumn,
            endLineNumber: d.endLine,
            endColumn: Math.max(d.endColumn, d.startColumn + 1),
        }));
        monaco.editor.setModelMarkers(editor.getModel(), "sigil", markers);
    }

    // ---- Audio output -------------------------------------------------------------------------

    const LEAD_SECONDS = 0.2; // how far ahead the pump keeps the worklet fed

    const audio = {
        context: null,
        node: null,
        analyser: null,
        engine: null,
        written: 0,
        read: 0,
        underruns: 0,

        async start(engine) {
            audio.engine = engine;
            if (!audio.context) {
                const context = new AudioContext({ latencyHint: "interactive" });
                await context.audioWorklet.addModule("js/output-worklet.js");
                const node = new AudioWorkletNode(context, "sigil-output", { numberOfInputs: 0, outputChannelCount: [2] });
                node.port.onmessage = e => { audio.read = e.data.read; audio.underruns = e.data.underruns; };
                const analyser = context.createAnalyser();
                analyser.fftSize = 2048;
                node.connect(analyser);
                analyser.connect(context.destination);
                Object.assign(audio, { context, node, analyser });
                setInterval(audio.pump, 25);
                startScope();
            }
            await audio.context.resume();
            return audio.context.sampleRate;
        },

        buffered() {
            return Math.max(0, audio.written - audio.read);
        },

        // Discards queued audio beyond `seconds`, so a new sound is heard almost at once.
        flush(seconds) {
            if (!audio.node) return;
            const keep = Math.round(seconds * audio.context.sampleRate);
            audio.node.port.postMessage({ flush: keep });
            audio.written = Math.min(audio.written, audio.read + keep);
        },

        push(bytes) {
            if (!audio.node || !bytes || bytes.length === 0) return;
            // Copy into a fresh, aligned buffer we can hand to the worklet without copying again.
            const samples = new Float32Array(bytes.byteLength / 4);
            new Uint8Array(samples.buffer).set(bytes);
            audio.written += samples.length / 2;   // count before the transfer empties the array
            audio.node.port.postMessage(samples, [samples.buffer]);
        },

        pump() {
            if (!audio.engine || audio.context.state !== "running") return;
            const missing = Math.round(audio.context.sampleRate * LEAD_SECONDS) - audio.buffered();
            if (missing >= 256)
                audio.push(audio.engine.invokeMethod("Render", missing));
        },
    };

    // ---- Oscilloscope -------------------------------------------------------------------------

    function startScope() {
        const canvas = document.getElementById("scope");
        if (!canvas) return;
        const g = canvas.getContext("2d");
        const data = new Float32Array(audio.analyser.fftSize);
        const css = getComputedStyle(document.documentElement);
        const teal = css.getPropertyValue("--teal").trim();
        const rule = css.getPropertyValue("--line-2").trim();

        function draw() {
            requestAnimationFrame(draw);
            const scale = window.devicePixelRatio || 1;
            const w = canvas.clientWidth, h = canvas.clientHeight;
            if (canvas.width !== w * scale || canvas.height !== h * scale) {
                canvas.width = w * scale;
                canvas.height = h * scale;
            }
            g.setTransform(scale, 0, 0, scale, 0, 0);
            g.clearRect(0, 0, w, h);

            g.strokeStyle = rule;
            g.lineWidth = 1;
            g.beginPath();
            g.moveTo(0, h / 2);
            g.lineTo(w, h / 2);
            g.stroke();

            audio.analyser.getFloatTimeDomainData(data);
            // Start at a rising zero crossing so periodic sounds stand still.
            let start = 0;
            for (let i = 1; i < data.length / 2; i++) {
                if (data[i - 1] < 0 && data[i] >= 0) { start = i; break; }
            }
            const count = data.length / 2;
            g.strokeStyle = teal;
            g.lineWidth = 1.5;
            g.beginPath();
            for (let i = 0; i < count; i++) {
                const x = (i / (count - 1)) * w;
                const y = h / 2 - data[start + i] * (h / 2 - 4);
                if (i === 0) g.moveTo(x, y); else g.lineTo(x, y);
            }
            g.stroke();
        }
        draw();
    }

    function initResizer() {
        const resizer = document.getElementById("resizer");
        const work = document.querySelector(".work");
        if (!resizer || !work) return;

        const MIN_WIDTH = 220;
        const MAX_WIDTH = 800;

        // Restore saved sidebar width if any
        const savedWidth = window.sigilRepl.prefs.get("sidebarWidth", null);
        if (savedWidth && typeof savedWidth === "number") {
            const clamped = Math.max(MIN_WIDTH, Math.min(MAX_WIDTH, savedWidth));
            work.style.setProperty("--sidebar-width", `${clamped}px`);
        }

        let isDragging = false;

        function onPointerDown(e) {
            isDragging = true;
            resizer.classList.add("resizing");
            document.body.style.cursor = "col-resize";
            document.body.style.userSelect = "none";
            resizer.setPointerCapture(e.pointerId);
        }

        function onPointerMove(e) {
            if (!isDragging) return;
            const workRect = work.getBoundingClientRect();
            // Calculate new width: distance from mouse pointer to right edge of .work
            const newWidth = workRect.right - e.clientX;
            const clamped = Math.max(MIN_WIDTH, Math.min(MAX_WIDTH, Math.min(newWidth, workRect.width - 200)));
            work.style.setProperty("--sidebar-width", `${clamped}px`);
            if (editor) editor.layout();
        }

        function onPointerUp(e) {
            if (!isDragging) return;
            isDragging = false;
            resizer.classList.remove("resizing");
            document.body.style.cursor = "";
            document.body.style.userSelect = "";
            if (resizer.hasPointerCapture(e.pointerId)) {
                resizer.releasePointerCapture(e.pointerId);
            }

            // Save preference
            const currentStr = work.style.getPropertyValue("--sidebar-width");
            const parsed = parseInt(currentStr, 10);
            if (!isNaN(parsed)) {
                window.sigilRepl.prefs.set("sidebarWidth", parsed);
            }
        }

        resizer.addEventListener("pointerdown", onPointerDown);
        resizer.addEventListener("pointermove", onPointerMove);
        resizer.addEventListener("pointerup", onPointerUp);
        resizer.addEventListener("pointercancel", onPointerUp);

        // Keyboard accessibility: Left / Right arrow keys resize by 20px
        resizer.addEventListener("keydown", e => {
            const currentStr = work.style.getPropertyValue("--sidebar-width") || "360px";
            let current = parseInt(currentStr, 10) || 360;
            if (e.key === "ArrowLeft") {
                e.preventDefault();
                current = Math.min(MAX_WIDTH, current + 20);
                work.style.setProperty("--sidebar-width", `${current}px`);
                window.sigilRepl.prefs.set("sidebarWidth", current);
                if (editor) editor.layout();
            } else if (e.key === "ArrowRight") {
                e.preventDefault();
                current = Math.max(MIN_WIDTH, current - 20);
                work.style.setProperty("--sidebar-width", `${current}px`);
                window.sigilRepl.prefs.set("sidebarWidth", current);
                if (editor) editor.layout();
            }
        });
    }

    // ---- Samples ------------------------------------------------------------------------------
    // WAV files the user drops on the page. C# keeps them in its virtual folder (that is what snippets read); this
    // file moves them in from the drop and keeps a copy in IndexedDB so they are there on the next visit.

    const samples = {
        dbPromise: null,
        kept: false,

        open() {
            samples.dbPromise ??= new Promise((resolve, reject) => {
                if (!window.indexedDB) return reject(new Error("IndexedDB is not available."));
                const request = indexedDB.open("sigil-repl", 1);
                request.onupgradeneeded = () => request.result.createObjectStore("samples", { keyPath: "name" });
                request.onsuccess = () => resolve(request.result);
                request.onerror = () => reject(request.error);
            });
            return samples.dbPromise;
        },

        async transact(mode, work) {
            const db = await samples.open();
            return new Promise((resolve, reject) => {
                const tx = db.transaction("samples", mode);
                const request = work(tx.objectStore("samples"));
                tx.oncomplete = () => resolve(request.result);
                tx.onerror = tx.onabort = () => reject(tx.error);
            });
        },

        // Storage can be refused (private windows, full disk); samples then last for this visit only.
        async save(name, bytes) {
            try { await samples.transact("readwrite", store => store.put({ name, bytes })); } catch { }
        },

        async forget(name) {
            try { await samples.transact("readwrite", store => store.delete(name)); } catch { }
        },

        // Puts the samples of earlier visits back into C#'s folder. Returns whether the browser keeps samples at all.
        async restore() {
            try {
                const saved = await samples.transact("readonly", store => store.getAll());
                for (const entry of saved) {
                    await page.invokeMethodAsync("RestoreSample", entry.name, entry.bytes);
                }
                samples.kept = true;
            } catch {
                samples.kept = false;
            }
            return samples.kept;
        },
    };

    async function addSampleFiles(files) {
        if (!page) return;
        for (const file of files) {
            // C# decides first, so a file that can never be kept is not read into memory.
            if (!(await page.invokeMethodAsync("CanAddSample", file.name, file.size))) continue;
            const bytes = new Uint8Array(await file.arrayBuffer());
            const result = await page.invokeMethodAsync("AddSample", file.name, bytes);
            if (result.ok) await samples.save(result.name, bytes);
        }
    }

    // Dropping files anywhere on the page adds them as samples; the overlay in index.html shows where they will go.
    function initSampleDrop() {
        const carriesFiles = e => !!e.dataTransfer && Array.from(e.dataTransfer.types).includes("Files");
        let depth = 0; // dragenter/dragleave fire for every element crossed, so count them

        window.addEventListener("dragenter", e => {
            if (!carriesFiles(e)) return;
            e.preventDefault();
            depth++;
            document.body.classList.add("dragging-files");
        }, true);

        window.addEventListener("dragover", e => {
            if (!carriesFiles(e)) return;
            e.preventDefault();
            e.stopPropagation();
            e.dataTransfer.dropEffect = "copy";
        }, true);

        window.addEventListener("dragleave", e => {
            if (!carriesFiles(e)) return;
            depth = Math.max(0, depth - 1);
            if (depth === 0) document.body.classList.remove("dragging-files");
        }, true);

        window.addEventListener("drop", e => {
            if (!carriesFiles(e)) return;
            e.preventDefault();
            e.stopPropagation();
            depth = 0;
            document.body.classList.remove("dragging-files");
            // The list is only valid during the event, so copy it before waiting for anything.
            addSampleFiles(Array.from(e.dataTransfer.files));
        }, true);

        const picker = document.getElementById("sample-picker");
        picker?.addEventListener("change", () => {
            const files = Array.from(picker.files);
            picker.value = "";
            addSampleFiles(files);
        });
    }

    // ---- API used from C# --------------------------------------------------------------------

    window.sigilRepl = {
        audio,
        samples,

        // Types text at the cursor, replacing the selection. Used to insert Samples.Load("...") from the sample list.
        insertText(text) {
            editor.executeEdits("samples", [{ range: editor.getSelection(), text, forceMoveMarkers: true }]);
            editor.focus();
        },

        async init(pageRef, host, fallbackCode) {
            page = pageRef;
            const monaco = await loadMonaco();
            defineTheme(monaco);
            registerLanguageServices(monaco);

            editor = monaco.editor.create(host, {
                value: codeFromUrl() ?? fallbackCode,
                language: "csharp",
                theme: "sigil",
                automaticLayout: true,
                fontFamily: "'Roboto Mono', ui-monospace, Consolas, monospace",
                fontSize: 14,
                lineHeight: 22,
                minimap: { enabled: false },
                scrollBeyondLastLine: false,
                renderLineHighlight: "line",
                padding: { top: 12 },
                tabSize: 4,
                suggest: { showWords: false, showStatusBar: false },
                hover: { delay: 350, above: false },
                quickSuggestionsDelay: 150,
            });

            editor.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyCode.Enter, () => page.invokeMethodAsync("RunAsync"));

            // Ctrl+Space is taken by launchers such as Everything on Windows and never reaches the page,
            // so suggestions and parameter hints get keys that are free everywhere.
            editor.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyCode.KeyI, () => editor.trigger("keyboard", "editor.action.triggerSuggest", {}));
            editor.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyMod.Shift | monaco.KeyCode.KeyI, () => editor.trigger("keyboard", "editor.action.triggerParameterHints", {}));
            editor.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyCode.KeyQ, () => editor.trigger("keyboard", "editor.action.showHover", {}));
            editor.onMouseDown(e => {
                const modified = e.event.ctrlKey || e.event.metaKey;
                if (!modified || !e.target.position) return;
                editor.setPosition(e.target.position);
                setTimeout(() => editor.trigger("mouse", "editor.action.showHover", {}), 0);
            });
            // Ctrl+. is Monaco's quick fix, so stop is caught before the editor sees it, and works anywhere on the page.
            window.addEventListener("keydown", e => {
                if ((e.ctrlKey || e.metaKey) && e.key === ".") {
                    e.preventDefault();
                    e.stopPropagation();
                    page.invokeMethodAsync("Stop");
                }
            }, true);
            editor.onDidChangeModelContent(scheduleCheck);
            initResizer();
            initSampleDrop();
            editor.focus();
        },

        getCode: () => editor.getValue(),

        // Per-browser preferences; storage can be unavailable (private windows), so failures fall back to defaults.
        prefs: {
            get(key, fallback) {
                try {
                    const value = localStorage.getItem("sigil-repl." + key);
                    return value === null ? fallback : JSON.parse(value);
                } catch {
                    return fallback;
                }
            },
            set(key, value) {
                try { localStorage.setItem("sigil-repl." + key, JSON.stringify(value)); } catch { }
            },
        },

        setCode(code) {
            editor.setValue(code);
            editor.focus();
        },

        setMarkers,

        async share() {
            const bytes = new TextEncoder().encode(editor.getValue());
            let binary = "";
            bytes.forEach(b => binary += String.fromCharCode(b));
            const encoded = btoa(binary).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
            history.replaceState(null, "", "#code=" + encoded);
            try {
                await navigator.clipboard.writeText(location.href);
                return true;
            } catch {
                return false;
            }
        },

        download(name, bytes) {
            const url = URL.createObjectURL(new Blob([bytes], { type: "audio/wav" }));
            const link = Object.assign(document.createElement("a"), { href: url, download: name });
            link.click();
            setTimeout(() => URL.revokeObjectURL(url), 1000);
        },
    };
})();
