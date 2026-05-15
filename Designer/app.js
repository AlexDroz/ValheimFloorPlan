const DESIGNER_VERSION = "1.0.4";

const state = {
  cols: 20,
  rows: 20,
  pieces: [],
  currentPiece: "Floor2x2",
  rotation: 0,
  dirty: false,
  fileHandle: null,
  lastPickerHandle: null,
  fileName: "",
  hover: { col: -1, row: -1 },
  isCanvasHovered: false,
};

const pieceColors = {
  Floor2x2: "#b68d56",
  Floor1x1: "#d0ad78",
  Wall: "#8b8f99",
  Doorway: "#3aa65a",
  Pillar: "#2db6ab",
};

const pieceDefs = {
  Floor2x2: { w: 2, h: 2 },
  Floor1x1: { w: 1, h: 1 },
  Wall: { w: 2, h: 1 },
  Doorway: { w: 2, h: 1 },
  Pillar: { w: 1, h: 1 },
};

const canvas = document.getElementById("gridCanvas");
const ctx = canvas.getContext("2d");

// Shared picker id lets supported browsers remember the last used folder
// for this app's open/save operations.
const VFP_PICKER_ID = "valheim-floorplanner-vfp-files";

const statusEl = document.getElementById("status");
const colsInput = document.getElementById("colsInput");
const rowsInput = document.getElementById("rowsInput");
const rotSelect = document.getElementById("rotSelect");
const pieceTypeRadios = document.querySelectorAll('input[name="pieceType"]');

// Keep version visible in browser tab from a single source of truth.
document.title = `Valheim Floor Plan Designer v${DESIGNER_VERSION} (Web)`;

function initPieceSwatches() {
  const swatches = document.querySelectorAll(".piece-swatch[data-piece]");
  for (const swatch of swatches) {
    const pieceType = swatch.getAttribute("data-piece");
    swatch.style.backgroundColor = pieceColors[pieceType] || "#999999";
  }
}

function setStatus(msg) {
  statusEl.textContent = msg;
}

function markDirty(isDirty = true) {
  state.dirty = isDirty;
  const file = state.fileName || "Untitled";
  setStatus(`${file}${state.dirty ? " *" : ""}`);
}

function effectiveSize(type, rotation) {
  const base = pieceDefs[type] || { w: 1, h: 1 };
  return rotation === 90 || rotation === 270
    ? { w: base.h, h: base.w }
    : { w: base.w, h: base.h };
}

function gridLayout() {
  const pad = 30;
  const usableW = canvas.width - pad * 2;
  const usableH = canvas.height - pad * 2;
  const cell = Math.max(8, Math.floor(Math.min(usableW / state.cols, usableH / state.rows)));
  const gridW = cell * state.cols;
  const gridH = cell * state.rows;
  const originX = Math.floor((canvas.width - gridW) / 2);
  const originY = Math.floor((canvas.height - gridH) / 2);
  return { pad, cell, gridW, gridH, originX, originY };
}

function getOverlapInfo() {
  const counts = Array.from({ length: state.rows }, () => Array(state.cols).fill(0));

  for (const p of state.pieces) {
    if (isFloorType(p.type)) continue;
    const { w, h } = effectiveSize(p.type, p.rot);
    for (let r = p.row; r < p.row + h; r += 1) {
      for (let c = p.col; c < p.col + w; c += 1) {
        if (r < 0 || r >= state.rows || c < 0 || c >= state.cols) continue;
        counts[r][c] += 1;
      }
    }
  }

  const topOverlappingPieceIndexes = new Set();
  for (let r = 0; r < state.rows; r += 1) {
    for (let c = 0; c < state.cols; c += 1) {
      if (counts[r][c] <= 1) continue;

      let bestIndex = -1;
      let bestLayer = -1;
      for (let i = 0; i < state.pieces.length; i += 1) {
        const p = state.pieces[i];
        if (isFloorType(p.type)) continue;
        if (!pieceOccupiesCell(p, c, r)) continue;

        const layer = pieceLayerOrder(p.type);
        if (layer > bestLayer || (layer === bestLayer && i > bestIndex)) {
          bestLayer = layer;
          bestIndex = i;
        }
      }

      if (bestIndex >= 0) {
        topOverlappingPieceIndexes.add(bestIndex);
      }
    }
  }

  return { topOverlappingPieceIndexes };
}

function rotateActivePiece(step) {
  state.rotation = (state.rotation + step + 360) % 360;
  rotSelect.value = String(state.rotation);
  draw();
}

function pieceLayerOrder(type) {
  switch (type) {
    case "Floor2x2":
    case "Floor1x1":
      return 1;
    case "Wall":
      return 2;
    case "Doorway":
      return 3;
    case "Pillar":
      return 4;
    default:
      return 99;
  }
}

function isFloorType(type) {
  return type === "Floor2x2" || type === "Floor1x1";
}

function lightenHexColor(hex, amount = 0.55) {
  const raw = (hex || "").replace("#", "");
  if (!/^[0-9a-fA-F]{6}$/.test(raw)) return { r: 220, g: 220, b: 220 };

  const baseR = parseInt(raw.slice(0, 2), 16);
  const baseG = parseInt(raw.slice(2, 4), 16);
  const baseB = parseInt(raw.slice(4, 6), 16);

  return {
    r: Math.round(baseR + (255 - baseR) * amount),
    g: Math.round(baseG + (255 - baseG) * amount),
    b: Math.round(baseB + (255 - baseB) * amount),
  };
}

function pieceOccupiesCell(piece, col, row) {
  const { w, h } = effectiveSize(piece.type, piece.rot);
  return col >= piece.col && col < piece.col + w && row >= piece.row && row < piece.row + h;
}

function findTopMostPieceIndexAtCell(col, row) {
  let bestIndex = -1;
  let bestLayer = -1;

  for (let i = 0; i < state.pieces.length; i += 1) {
    const piece = state.pieces[i];
    if (!pieceOccupiesCell(piece, col, row)) continue;

    const layer = pieceLayerOrder(piece.type);
    if (layer > bestLayer || (layer === bestLayer && i > bestIndex)) {
      bestLayer = layer;
      bestIndex = i;
    }
  }

  return bestIndex;
}

function canvasEventToGridCell(ev) {
  const rect = canvas.getBoundingClientRect();
  const scaleX = canvas.width / rect.width;
  const scaleY = canvas.height / rect.height;
  const x = (ev.clientX - rect.left) * scaleX;
  const y = (ev.clientY - rect.top) * scaleY;
  const { cell, originX, originY } = gridLayout();
  const col = Math.floor((x - originX) / cell);
  const row = Math.floor((y - originY) / cell);
  return { col, row };
}

function draw() {
  const { cell, gridW, gridH, originX, originY } = gridLayout();
  const { topOverlappingPieceIndexes } = getOverlapInfo();

  ctx.clearRect(0, 0, canvas.width, canvas.height);

  ctx.fillStyle = "#1d1a17";
  ctx.fillRect(originX, originY, gridW, gridH);

  for (let c = 0; c <= state.cols; c += 1) {
    const x = originX + c * cell;
    ctx.strokeStyle = "#5a4f43";
    ctx.lineWidth = 1;
    ctx.beginPath();
    ctx.moveTo(x, originY);
    ctx.lineTo(x, originY + gridH);
    ctx.stroke();
  }

  for (let r = 0; r <= state.rows; r += 1) {
    const y = originY + r * cell;
    ctx.strokeStyle = "#5a4f43";
    ctx.lineWidth = 1;
    ctx.beginPath();
    ctx.moveTo(originX, y);
    ctx.lineTo(originX + gridW, y);
    ctx.stroke();
  }

  const orderedPieces = [...state.pieces].sort((a, b) => pieceLayerOrder(a.type) - pieceLayerOrder(b.type));
  for (const p of orderedPieces) {
    const { w, h } = effectiveSize(p.type, p.rot);
    const x = originX + p.col * cell;
    const y = originY + p.row * cell;
    ctx.fillStyle = pieceColors[p.type] || "#999";
    ctx.fillRect(x + 1, y + 1, w * cell - 2, h * cell - 2);
    ctx.strokeStyle = "rgba(0, 0, 0, 0.35)";
    ctx.strokeRect(x + 1, y + 1, w * cell - 2, h * cell - 2);
  }

  for (const pieceIndex of topOverlappingPieceIndexes) {
    const p = state.pieces[pieceIndex];
    const { w, h } = effectiveSize(p.type, p.rot);
    const x = originX + p.col * cell;
    const y = originY + p.row * cell;
    ctx.strokeStyle = "#ff7a7a";
    ctx.lineWidth = 3;
    ctx.setLineDash([8, 5]);
    ctx.strokeRect(x + 1, y + 1, w * cell - 2, h * cell - 2);
  }
  ctx.setLineDash([]);

  if (state.hover.col >= 0 && state.hover.row >= 0) {
    const { w, h } = effectiveSize(state.currentPiece, state.rotation);
    const x = originX + state.hover.col * cell;
    const y = originY + state.hover.row * cell;
    const hoverBase = pieceColors[state.currentPiece] || "#999999";
    const hoverTint = lightenHexColor(hoverBase, 0.6);
    ctx.fillStyle = `rgba(${hoverTint.r}, ${hoverTint.g}, ${hoverTint.b}, 0.38)`;
    ctx.fillRect(x, y, w * cell, h * cell);
  }
}

function parseVfp(text) {
  const next = { cols: 20, rows: 20, pieces: [] };
  const lines = text.split(/\r?\n/);

  for (const raw of lines) {
    const line = raw.trim();
    if (!line) continue;

    if (line.startsWith("cols=")) {
      next.cols = Math.max(1, parseInt(line.slice(5), 10) || 20);
      continue;
    }

    if (line.startsWith("rows=")) {
      next.rows = Math.max(1, parseInt(line.slice(5), 10) || 20);
      continue;
    }

    if (line.startsWith("piece,")) {
      const parts = line.split(",");
      if (parts.length < 4) continue;
      const col = parseInt(parts[1], 10);
      const row = parseInt(parts[2], 10);
      const type = parts[3];
      const rot = parts.length > 4 ? (parseInt(parts[4], 10) || 0) : 0;
      if (!Number.isInteger(col) || !Number.isInteger(row)) continue;
      next.pieces.push({ col, row, type, rot });
    }
  }

  return next;
}

function serializeVfp() {
  const lines = [`cols=${state.cols}`, `rows=${state.rows}`];
  for (const p of state.pieces) {
    lines.push(`piece,${p.col},${p.row},${p.type},${p.rot || 0}`);
  }
  return `${lines.join("\n")}\n`;
}

async function openVfp() {
  try {
    if (window.showOpenFilePicker) {
      const openOptions = {
        id: VFP_PICKER_ID,
        types: [{ description: "Valheim Floor Plan", accept: { "text/plain": [".vfp"] } }],
        multiple: false,
      };
      openOptions.startIn = state.lastPickerHandle || "documents";

      const [handle] = await window.showOpenFilePicker({
        ...openOptions,
      });
      const file = await handle.getFile();
      const text = await file.text();
      const parsed = parseVfp(text);
      state.cols = parsed.cols;
      state.rows = parsed.rows;
      state.pieces = parsed.pieces;
      state.fileHandle = handle;
      state.lastPickerHandle = handle;
      state.fileName = file.name;
      colsInput.value = String(state.cols);
      rowsInput.value = String(state.rows);
      markDirty(false);
      draw();
      return;
    }

    const input = document.createElement("input");
    input.type = "file";
    input.accept = ".vfp,text/plain";
    input.onchange = async () => {
      const file = input.files?.[0];
      if (!file) return;
      const text = await file.text();
      const parsed = parseVfp(text);
      state.cols = parsed.cols;
      state.rows = parsed.rows;
      state.pieces = parsed.pieces;
      state.fileHandle = null;
      state.fileName = file.name;
      colsInput.value = String(state.cols);
      rowsInput.value = String(state.rows);
      markDirty(false);
      draw();
    };
    input.click();
  } catch (err) {
    setStatus(`Open canceled or failed: ${String(err)}`);
  }
}

async function saveAsVfp() {
  const data = serializeVfp();

  try {
    if (window.showSaveFilePicker) {
      const saveOptions = {
        id: VFP_PICKER_ID,
        suggestedName: state.fileName || "myfloorplan.vfp",
        types: [{ description: "Valheim Floor Plan", accept: { "text/plain": [".vfp"] } }],
      };
      saveOptions.startIn = state.lastPickerHandle || state.fileHandle || "documents";

      const handle = await window.showSaveFilePicker({
        ...saveOptions,
      });
      const writable = await handle.createWritable();
      await writable.write(data);
      await writable.close();

      state.fileHandle = handle;
      state.lastPickerHandle = handle;
      state.fileName = (await handle.getFile()).name;
      markDirty(false);
      return;
    }

    const blob = new Blob([data], { type: "text/plain" });
    const a = document.createElement("a");
    a.href = URL.createObjectURL(blob);
    a.download = state.fileName || "myfloorplan.vfp";
    a.click();
    URL.revokeObjectURL(a.href);
    markDirty(false);
  } catch (err) {
    setStatus(`Save As canceled or failed: ${String(err)}`);
  }
}

async function saveVfp() {
  const data = serializeVfp();
  try {
    if (state.fileHandle) {
      const writable = await state.fileHandle.createWritable();
      await writable.write(data);
      await writable.close();
      markDirty(false);
      return;
    }
    await saveAsVfp();
  } catch (err) {
    setStatus(`Save failed: ${String(err)}`);
  }
}

function confirmDirty(actionName) {
  if (!state.dirty) return true;
  return window.confirm(`You have unsaved changes. Continue with ${actionName}?`);
}

function newPlan() {
  if (!confirmDirty("New")) return;
  state.cols = Math.max(1, parseInt(colsInput.value, 10) || 20);
  state.rows = Math.max(1, parseInt(rowsInput.value, 10) || 20);
  state.pieces = [];
  state.fileHandle = null;
  state.fileName = "";
  markDirty(false);
  draw();
}

function applyGrid() {
  state.cols = Math.max(1, parseInt(colsInput.value, 10) || 20);
  state.rows = Math.max(1, parseInt(rowsInput.value, 10) || 20);
  state.pieces = state.pieces.filter((p) => p.col < state.cols && p.row < state.rows);
  markDirty(true);
  draw();
}

function clearGrid() {
  if (state.pieces.length === 0) return;
  state.pieces = [];
  markDirty(true);
  draw();
}

function removeTopMostAt(col, row) {
  const idx = findTopMostPieceIndexAtCell(col, row);
  if (idx < 0) return;
  state.pieces.splice(idx, 1);
  markDirty(true);
}

function addAt(col, row) {
  const { w, h } = effectiveSize(state.currentPiece, state.rotation);
  if (col < 0 || row < 0) return;
  if (col + w > state.cols || row + h > state.rows) return;
  state.pieces.push({ col, row, type: state.currentPiece, rot: state.rotation });
  markDirty(true);
}

canvas.addEventListener("mousemove", (ev) => {
  state.isCanvasHovered = true;
  const { col, row } = canvasEventToGridCell(ev);
  if (col >= 0 && col < state.cols && row >= 0 && row < state.rows) {
    state.hover.col = col;
    state.hover.row = row;
  } else {
    state.hover.col = -1;
    state.hover.row = -1;
  }
  draw();
});

canvas.addEventListener("mouseleave", () => {
  state.isCanvasHovered = false;
  state.hover.col = -1;
  state.hover.row = -1;
  draw();
});

canvas.addEventListener("mouseenter", () => {
  state.isCanvasHovered = true;
});

canvas.addEventListener("click", () => {
  if (state.hover.col < 0 || state.hover.row < 0) return;
  addAt(state.hover.col, state.hover.row);
  draw();
});

canvas.addEventListener("contextmenu", (ev) => {
  ev.preventDefault();
  const { col, row } = canvasEventToGridCell(ev);
  if (col < 0 || col >= state.cols || row < 0 || row >= state.rows) return;
  removeTopMostAt(col, row);
  draw();
});

function cmdShell() {
  state.pieces = [];

  const cols = state.cols;
  const rows = state.rows;

  // 1. Fill with Floor2x2 tiles, anchor stepping by 2
  for (let r = 0; r <= rows - 2; r += 2) {
    for (let c = 0; c <= cols - 2; c += 2) {
      state.pieces.push({ col: c, row: r, type: "Floor2x2", rot: 0 });
    }
  }

  // 2. Doorway anchor positions (centered on each edge)
  const doorTopCol  = Math.floor((cols - 2) / 2);
  const doorLeftRow = Math.floor((rows - 2) / 2);

  const doorways = [
    { col: doorTopCol, row: rows - 1, type: "Doorway", rot: 0 },
    { col: doorTopCol, row: 0, type: "Doorway", rot: 180 },
    { col: 0, row: doorLeftRow, type: "Doorway", rot: 270 },
    { col: cols - 1, row: doorLeftRow, type: "Doorway", rot: 90 },
  ];

  function footprintsOverlap(aCol, aRow, aW, aH, bCol, bRow, bW, bH) {
    return aCol < bCol + bW && aCol + aW > bCol && aRow < bRow + bH && aRow + aH > bRow;
  }

  function wallOverlapsDoorway(col, row, rot) {
    const wallSize = effectiveSize("Wall", rot);
    for (const door of doorways) {
      const doorSize = effectiveSize("Doorway", door.rot);
      if (
        footprintsOverlap(
          col,
          row,
          wallSize.w,
          wallSize.h,
          door.col,
          door.row,
          doorSize.w,
          doorSize.h
        )
      ) {
        return true;
      }
    }
    return false;
  }

  // 3. Doorways first
  for (const door of doorways) {
    state.pieces.push(door);
  }

  // 4. Top edge walls (rot 0 = outer face top), step col by 2
  for (let c = 0; c <= cols - 2; c += 2) {
    if (wallOverlapsDoorway(c, rows - 1, 0)) continue;
    state.pieces.push({ col: c, row: rows - 1, type: "Wall", rot: 0 });
  }

  // 5. Bottom edge walls (rot 180 = outer face bottom), step col by 2
  for (let c = 0; c <= cols - 2; c += 2) {
    if (wallOverlapsDoorway(c, 0, 180)) continue;
    state.pieces.push({ col: c, row: 0, type: "Wall", rot: 180 });
  }

  // 6. Left edge walls (rot 270 = outer face left), step row by 2
  for (let r = 0; r <= rows - 2; r += 2) {
    if (wallOverlapsDoorway(0, r, 270)) continue;
    state.pieces.push({ col: 0, row: r, type: "Wall", rot: 270 });
  }

  // 7. Right edge walls (rot 90 = outer face right), step row by 2
  for (let r = 0; r <= rows - 2; r += 2) {
    if (wallOverlapsDoorway(cols - 1, r, 90)) continue;
    state.pieces.push({ col: cols - 1, row: r, type: "Wall", rot: 90 });
  }

  // 8. Pillars flanking each doorway (may overlap walls, must not displace doors)
  // Top door flanks
  if (doorTopCol - 1 >= 0)           state.pieces.push({ col: doorTopCol - 1, row: rows - 1, type: "Pillar", rot: 0 });
  if (doorTopCol + 2 <= cols - 1)    state.pieces.push({ col: doorTopCol + 2, row: rows - 1, type: "Pillar", rot: 0 });
  // Bottom door flanks
  if (doorTopCol - 1 >= 0)           state.pieces.push({ col: doorTopCol - 1, row: 0, type: "Pillar", rot: 0 });
  if (doorTopCol + 2 <= cols - 1)    state.pieces.push({ col: doorTopCol + 2, row: 0, type: "Pillar", rot: 0 });
  // Left door flanks
  if (doorLeftRow - 1 >= 0)          state.pieces.push({ col: 0, row: doorLeftRow - 1, type: "Pillar", rot: 0 });
  if (doorLeftRow + 2 <= rows - 1)   state.pieces.push({ col: 0, row: doorLeftRow + 2, type: "Pillar", rot: 0 });
  // Right door flanks
  if (doorLeftRow - 1 >= 0)          state.pieces.push({ col: cols - 1, row: doorLeftRow - 1, type: "Pillar", rot: 0 });
  if (doorLeftRow + 2 <= rows - 1)   state.pieces.push({ col: cols - 1, row: doorLeftRow + 2, type: "Pillar", rot: 0 });

  // 9. Corner pillars
  state.pieces.push({ col: 0,        row: rows - 1, type: "Pillar", rot: 0 });
  state.pieces.push({ col: cols - 1, row: rows - 1, type: "Pillar", rot: 0 });
  state.pieces.push({ col: 0,        row: 0,        type: "Pillar", rot: 0 });
  state.pieces.push({ col: cols - 1, row: 0,        type: "Pillar", rot: 0 });

  markDirty(true);
  draw();
}

document.getElementById("newBtn").addEventListener("click", newPlan);
document.getElementById("clearBtn").addEventListener("click", clearGrid);
document.getElementById("openBtn").addEventListener("click", async () => {
  if (!confirmDirty("Open")) return;
  await openVfp();
});
document.getElementById("saveBtn").addEventListener("click", saveVfp);
document.getElementById("saveAsBtn").addEventListener("click", saveAsVfp);
document.getElementById("helpBtn").addEventListener("click", () => {
  window.open("help.html", "_blank", "noopener,noreferrer");
});
document.getElementById("applyGridBtn").addEventListener("click", applyGrid);
document.getElementById("shellBtn").addEventListener("click", cmdShell);

for (const radio of pieceTypeRadios) {
  radio.addEventListener("change", () => {
    if (!radio.checked) return;
    state.currentPiece = radio.value;
    draw();
  });
}

rotSelect.addEventListener("change", () => {
  state.rotation = parseInt(rotSelect.value, 10) || 0;
  draw();
});

window.addEventListener("keydown", (ev) => {
  const isArrow = ev.key === "ArrowRight" || ev.key === "ArrowUp" || ev.key === "ArrowLeft" || ev.key === "ArrowDown";
  if (!isArrow) return;

  // Canvas-hover takes precedence: rotate piece even if a radio keeps keyboard focus.
  if (state.isCanvasHovered) {
    ev.preventDefault();
    if (ev.key === "ArrowRight" || ev.key === "ArrowUp") {
      rotateActivePiece(90);
    } else {
      rotateActivePiece(-90);
    }
    return;
  }

  // Outside the canvas, keep normal keyboard behavior for form controls.
  const target = ev.target;
  if (target instanceof HTMLInputElement || target instanceof HTMLSelectElement || target instanceof HTMLTextAreaElement) {
    return;
  }
});

window.addEventListener("beforeunload", (ev) => {
  if (!state.dirty) return;
  ev.preventDefault();
  ev.returnValue = "";
});

initPieceSwatches();
draw();
markDirty(false);
