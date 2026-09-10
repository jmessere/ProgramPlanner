// Generic drag/resize support for the Gantt views. Bars are absolutely
// positioned <div class="gantt-bar"> elements with a data-id attribute and
// left/width set in pixels. Dragging the bar body moves it; dragging the
// ".resize-handle" child resizes it. On mouseup, the new left/width (in
// pixels) is reported back to Blazor which converts pixels -> dates and
// persists the change.
window.wmsGantt = {
    _state: null,
    init: function (containerId, dotNetRef) {
        const container = document.getElementById(containerId);
        if (!container || container.dataset.wmsGanttBound === "1") return;
        container.dataset.wmsGanttBound = "1";

        const onMouseDown = (e) => {
            const bar = e.target.closest(".gantt-bar");
            if (!bar || !container.contains(bar)) return;
            const isResize = e.target.classList.contains("resize-handle");
            window.wmsGantt._state = {
                bar,
                mode: isResize ? "resize" : "move",
                startX: e.clientX,
                origLeft: parseFloat(bar.style.left) || 0,
                origWidth: parseFloat(bar.style.width) || 0
            };
            e.preventDefault();
        };

        const onMouseMove = (e) => {
            const s = window.wmsGantt._state;
            if (!s) return;
            const dx = e.clientX - s.startX;
            if (s.mode === "move") {
                s.bar.style.left = Math.max(0, s.origLeft + dx) + "px";
            } else {
                s.bar.style.width = Math.max(8, s.origWidth + dx) + "px";
            }
        };

        const onMouseUp = async (e) => {
            const s = window.wmsGantt._state;
            if (!s) return;
            window.wmsGantt._state = null;
            const id = parseInt(s.bar.dataset.id, 10);
            const left = parseFloat(s.bar.style.left) || 0;
            const width = parseFloat(s.bar.style.width) || 0;
            await dotNetRef.invokeMethodAsync("HandleBarChanged", id, left, width);
        };

        container.addEventListener("mousedown", onMouseDown);
        document.addEventListener("mousemove", onMouseMove);
        document.addEventListener("mouseup", onMouseUp);
    }
};

// Global Ctrl+Z (Cmd+Z) handling for the Undo bar (SPEC.md Phase 22).
// Only one UndoBar instance is expected to be alive per page; a fresh
// listener replaces the previous one each time init() runs.
window.wmsUndo = {
    _dotNetRef: null,
    _bound: false,
    init: function (dotNetRef) {
        window.wmsUndo._dotNetRef = dotNetRef;
        if (window.wmsUndo._bound) return;
        window.wmsUndo._bound = true;
        document.addEventListener("keydown", function (e) {
            const isUndo = (e.ctrlKey || e.metaKey) && (e.key === "z" || e.key === "Z");
            if (!isUndo || !window.wmsUndo._dotNetRef) return;
            e.preventDefault();
            window.wmsUndo._dotNetRef.invokeMethodAsync("HandleUndoAsync");
        });
    }
};
