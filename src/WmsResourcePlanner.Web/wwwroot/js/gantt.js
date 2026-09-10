window.wmsGridNav = {
    // Every navigable cell in the grid - the frozen Team/Template/Workstream/
    // Role/Person <select> dropdowns as well as the scrolling month
    // input.fte-input cells - is treated as one flat, row-major list so
    // arrow keys move seamlessly across both frozen and scrolling columns.
    NAV_SELECTOR: "select.grid-nav-cell, input.fte-input",

    init: function (containerId) {
        const container = document.getElementById(containerId);
        if (!container || container.dataset.wmsGridNavBound === "1") return;
        container.dataset.wmsGridNavBound = "1";
        const NAV_SELECTOR = window.wmsGridNav.NAV_SELECTOR;

        container.addEventListener("keydown", function (e) {
            const cell = e.target.closest(NAV_SELECTOR);
            if (!cell) return;

            const cells = Array.from(container.querySelectorAll(NAV_SELECTOR));
            const idx = cells.indexOf(cell);
            if (idx === -1) return;

            // Each logical grid row (existing rows and the always-present
            // new-row) has the same number of nav cells: the visible frozen
            // columns plus one cell per month, so a single row-length lets
            // Up/Down jump to the same column in the row above/below.
            const cols = parseInt(container.dataset.rowCols, 10) || 1;
            let target = -1;

            // input[type=number] does not reliably support selectionStart/
            // selectionEnd across browsers (it's often null or throws), so
            // boundary-aware cursor movement isn't feasible here - Left/Right
            // always jump to the neighboring cell, same as Up/Down/Enter.
            // For <select> cells, ArrowUp/ArrowDown would otherwise change
            // the selected option natively - preventDefault below overrides
            // that so all cell types behave consistently as a grid.
            switch (e.key) {
                case "ArrowRight":
                    target = idx + 1;
                    break;
                case "ArrowLeft":
                    target = idx - 1;
                    break;
                case "ArrowDown":
                    target = idx + cols;
                    break;
                case "ArrowUp":
                    target = idx - cols;
                    break;
                case "Enter":
                    target = idx + cols;
                    break;
                default:
                    return;
            }

            if (target >= 0 && target < cells.length) {
                e.preventDefault();
                const next = cells[target];
                next.focus();
                if (typeof next.select === "function" && next.tagName === "INPUT") {
                    next.select();
                }
            }
        });
    }
};

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

// Resizable grid columns (Resource Plan). Each resizable header has a
// ".col-resize-handle" child with a "data-col" key (e.g. "team", "month").
// While dragging, a thin vertical guide line follows the cursor for visual
// feedback (touching every cell in the dragged column live would require a
// lot of DOM churn for little benefit in a server-rendered grid); the actual
// new width is only reported back to Blazor - which owns the authoritative
// per-column width state - once the mouse is released, triggering one
// re-render with the column at its final size.
window.wmsColResize = {
    _state: null,
    init: function (containerId, dotNetRef) {
        const container = document.getElementById(containerId);
        if (!container || container.dataset.wmsColResizeBound === "1") return;
        container.dataset.wmsColResizeBound = "1";

        const guide = document.createElement("div");
        guide.className = "col-resize-guide";
        guide.style.display = "none";
        document.body.appendChild(guide);

        const showGuide = (x) => {
            const rect = container.getBoundingClientRect();
            guide.style.left = x + "px";
            guide.style.top = rect.top + "px";
            guide.style.height = rect.height + "px";
            guide.style.display = "block";
        };

        const onMouseDown = (e) => {
            const handle = e.target.closest(".col-resize-handle");
            if (!handle || !container.contains(handle)) return;
            window.wmsColResize._state = {
                col: handle.dataset.col,
                startX: e.clientX
            };
            showGuide(e.clientX);
            e.preventDefault();
        };

        const onMouseMove = (e) => {
            const s = window.wmsColResize._state;
            if (!s) return;
            showGuide(e.clientX);
        };

        const onMouseUp = async (e) => {
            const s = window.wmsColResize._state;
            if (!s) return;
            window.wmsColResize._state = null;
            guide.style.display = "none";
            const deltaPx = e.clientX - s.startX;
            if (Math.abs(deltaPx) >= 1) {
                await dotNetRef.invokeMethodAsync("OnColumnResized", s.col, deltaPx);
            }
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
