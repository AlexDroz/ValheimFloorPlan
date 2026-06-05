const DESIGNER_VERSION = "2.0.3";

const MAX_LAYOUT_LEVELS = 3;
const WHEEL_ROTATION_STEP = 22.5;
const DEFAULT_ROTATION_STEP = 90;
const ROTATION_EPSILON = 0.001;

function createEmptyLayout() {
  return {
    cols: 20,
    rows: 20,
    pieces: [],
    dirty: false,
    fileHandle: null,
    lastPickerHandle: null,
    fileName: "",
  };
}

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
  activeLayoutIndex: 0,
  layouts: [createEmptyLayout(), createEmptyLayout(), createEmptyLayout()],
  overlayEnabled: [false, false, false],
  flexiWall: { phase: "idle", col1: 0, row1: 0, dragging: false, dragIndex: -1, dragMoved: false, dragJustEnded: false },
};

const pieceColors = {
  Floor2x2: "#b68d56",
  Floor1x1: "#d0ad78",
  Staircase: "#6d8f48",
  Bed: "#7b5c49",
  Workbench: "#8d5a32",
  Reserve: "#f59e0b",
  Hearth: "#8a4b3a",
  FlexiWall: "#8b8f99",
  Wall: "#8b8f99",
  Doorway: "#3aa65a",
  Pillar: "#2db6ab",
};

const levelOverlayStrokeColors = ["#f6d78b", "#8ec5ff", "#e1a5ff"];

const pieceDefs = {
  Floor2x2: { w: 2, h: 2 },
  Floor1x1: { w: 1, h: 1 },
  Staircase: { w: 4, h: 4 },
  Bed: { w: 2, h: 4 },
  Workbench: { w: 4, h: 4 },
  Reserve: { w: 1, h: 1 },
  Hearth: { w: 4, h: 3 },
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
const levelButtons = Array.from(document.querySelectorAll(".level-btn[data-level-index]"));
const overlayCheckboxes = Array.from(document.querySelectorAll("input[type=\"checkbox\"][data-overlay-index]"));
const activeLevelHintEl = document.getElementById("activeLevelHint");
const levelSummaryEls = [
  document.getElementById("level1Summary"),
  document.getElementById("level2Summary"),
  document.getElementById("level3Summary"),
];

// Keep version visible in browser tab from a single source of truth.
document.title = `Valheim Floor Plan Designer v${DESIGNER_VERSION} (Web)`;

function clonePieces(pieces) {
  return pieces.map((piece) => ({ ...piece }));
}

function getLayoutLabel(layoutIndex) {
  return `L${layoutIndex + 1}`;
}

function describeLayoutSummary(layoutIndex) {
  const layout = state.layouts[layoutIndex];
  const file = layout.fileName || "Untitled";
  return `${getLayoutLabel(layoutIndex)}: ${file} | ${layout.pieces.length} pieces | ${layout.cols}x${layout.rows}${layout.dirty ? " *" : ""}`;
}

function updateLevelSummaries() {
  for (let i = 0; i < levelSummaryEls.length; i += 1) {
    const el = levelSummaryEls[i];
    if (!el) continue;
    el.textContent = describeLayoutSummary(i);
    el.classList.toggle("is-active", i === state.activeLayoutIndex);
  }

  if (activeLevelHintEl) {
    activeLevelHintEl.textContent = `Active Level: ${state.activeLayoutIndex + 1}`;
  }
}

function persistActiveLayout() {
  const layout = state.layouts[state.activeLayoutIndex];
  layout.cols = state.cols;
  layout.rows = state.rows;
  layout.pieces = clonePieces(state.pieces);
  layout.dirty = state.dirty;
  layout.fileHandle = state.fileHandle;
  layout.lastPickerHandle = state.lastPickerHandle;
  layout.fileName = state.fileName;
}

function loadLayoutIntoState(layoutIndex) {
  const layout = state.layouts[layoutIndex];
  state.activeLayoutIndex = layoutIndex;
  state.cols = layout.cols;
  state.rows = layout.rows;
  state.pieces = clonePieces(layout.pieces);
  state.dirty = layout.dirty;
  state.fileHandle = layout.fileHandle;
  state.lastPickerHandle = layout.lastPickerHandle;
  state.fileName = layout.fileName;
  colsInput.value = String(state.cols);
  rowsInput.value = String(state.rows);
}

function updateLevelButtons() {
  for (let i = 0; i < levelButtons.length; i += 1) {
    const btn = levelButtons[i];
    const layout = state.layouts[i];
    const levelNumber = i + 1;
    const hasContent = layout.pieces.length > 0;
    btn.textContent = `Level ${levelNumber}`;
    btn.classList.toggle("is-active", i === state.activeLayoutIndex);
    btn.classList.toggle("has-content", hasContent);
    btn.classList.toggle("is-dirty", layout.dirty);
    btn.setAttribute("aria-selected", i === state.activeLayoutIndex ? "true" : "false");
    btn.title = layout.fileName || `Level ${levelNumber} (untitled)`;
  }

  updateLevelSummaries();
  updateOverlayControls();
}

function updateOverlayControls() {
  for (let i = 0; i < overlayCheckboxes.length; i += 1) {
    const cb = overlayCheckboxes[i];
    const index = parseInt(cb.getAttribute("data-overlay-index"), 10);
    if (!Number.isInteger(index)) continue;

    const isActive = index === state.activeLayoutIndex;
    cb.disabled = isActive;
    if (isActive) {
      cb.checked = false;
      state.overlayEnabled[index] = false;
      continue;
    }

    cb.checked = !!state.overlayEnabled[index];
  }
}

function switchLayout(layoutIndex) {
  if (layoutIndex < 0 || layoutIndex >= MAX_LAYOUT_LEVELS) return;
  if (layoutIndex === state.activeLayoutIndex) return;
  persistActiveLayout();
  loadLayoutIntoState(layoutIndex);
  state.hover.col = -1;
  state.hover.row = -1;
  updateLevelButtons();
  markDirty(state.dirty);
  draw();
}

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
  const levelLabel = `L${state.activeLayoutIndex + 1}`;
  const file = state.fileName || "Untitled";
  setStatus(`${levelLabel}: ${file}${state.dirty ? " *" : ""}`);
  persistActiveLayout();
  updateLevelButtons();
}

function effectiveSize(type, rotation) {
  const base = pieceDefs[type] || { w: 1, h: 1 };
  const normalized = normalizeAngle(rotation || 0);
  const quarterTurn =
    Math.abs(normalized - 90) < ROTATION_EPSILON ||
    Math.abs(normalized - 270) < ROTATION_EPSILON;
  return quarterTurn
    ? { w: base.h, h: base.w }
    : { w: base.w, h: base.h };
}

function normalizeAngle(angle) {
  let normalized = Number.isFinite(angle) ? angle : 0;
  normalized %= 360;
  if (normalized < 0) normalized += 360;
  return normalized;
}

function snapAngle(angle, step) {
  if (!Number.isFinite(step) || step <= 0) return normalizeAngle(angle);
  return normalizeAngle(Math.round(normalizeAngle(angle) / step) * step);
}

function formatAngleLabel(angle) {
  const rounded = Math.round(angle * 10) / 10;
  return Number.isInteger(rounded) ? String(rounded) : rounded.toFixed(1);
}

function getRotationStepForPieceType(pieceType) {
  return pieceType === "Workbench" ? WHEEL_ROTATION_STEP : DEFAULT_ROTATION_STEP;
}

function getRotationStepForCurrentTool() {
  return getRotationStepForPieceType(state.currentPiece);
}

function ensureRotationOptions(step = WHEEL_ROTATION_STEP) {
  const current = Number.parseFloat(rotSelect.value);
  const normalized = normalizeAngle(Number.isFinite(current) ? current : state.rotation);
  const options = [];
  for (let a = 0; a < 360; a += step) {
    options.push(formatAngleLabel(a));
  }

  rotSelect.innerHTML = options
    .map((label) => `<option value="${label}">${label}</option>`)
    .join("");

  const snapped = snapAngle(normalized, step);
  rotSelect.value = formatAngleLabel(snapped);
  state.rotation = snapped;
}

function syncRotationControlsForCurrentTool() {
  ensureRotationOptions(getRotationStepForCurrentTool());
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
  const topOverlappingPieceIndexes = new Set();
  const nonFloor = state.pieces
    .map((p, i) => ({ p, i }))
    .filter(({ p }) => !isFloorType(p.type) && p.type !== "FlexiWall");
  for (let a = 0; a < nonFloor.length - 1; a += 1) {
    for (let b = a + 1; b < nonFloor.length; b += 1) {
      if (obbOverlap(getPieceObb(nonFloor[a].p), getPieceObb(nonFloor[b].p))) {
        topOverlappingPieceIndexes.add(nonFloor[a].i);
        topOverlappingPieceIndexes.add(nonFloor[b].i);
      }
    }
  }
  return { topOverlappingPieceIndexes };
}

function getVisibleLayerIndexes() {
  const indexes = [state.activeLayoutIndex];
  for (let i = 0; i < state.layouts.length; i += 1) {
    if (i === state.activeLayoutIndex) continue;
    if (!state.overlayEnabled[i]) continue;
    indexes.push(i);
  }
  return indexes;
}

function shouldCountToolOverlap(pieceType) {
  return !isFloorType(pieceType) && pieceType !== "FlexiWall";
}

function extendsUpwardToHigherLevels(pieceType) {
  return pieceType === "Staircase" || pieceType === "Hearth";
}

function isDirectionalOverlapConflict(a, b) {
  if (a.layerIndex === b.layerIndex)
    return true;

  // Lower levels can only affect higher levels if the lower piece projects upward.
  if (a.layerIndex < b.layerIndex)
    return extendsUpwardToHigherLevels(a.pieceType);

  return extendsUpwardToHigherLevels(b.pieceType);
}

function getCrossLayerOverlapInfo() {
  const visibleLayerIndexes = getVisibleLayerIndexes();
  const marksByLayer = new Map();
  if (visibleLayerIndexes.length <= 1) return marksByLayer;

  const allPieces = [];
  for (const layerIndex of visibleLayerIndexes) {
    const layout = state.layouts[layerIndex];
    for (let pieceIndex = 0; pieceIndex < layout.pieces.length; pieceIndex += 1) {
      const piece = layout.pieces[pieceIndex];
      if (!shouldCountToolOverlap(piece.type)) continue;
      allPieces.push({ layerIndex, pieceIndex, pieceType: piece.type, obb: getPieceObb(piece) });
    }
  }

  for (let a = 0; a < allPieces.length - 1; a += 1) {
    for (let b = a + 1; b < allPieces.length; b += 1) {
      const occA = allPieces[a];
      const occB = allPieces[b];
      if (occA.layerIndex === occB.layerIndex) continue;
      if (!isDirectionalOverlapConflict(occA, occB)) continue;
      if (!obbOverlap(occA.obb, occB.obb)) continue;

      if (!marksByLayer.has(occA.layerIndex)) marksByLayer.set(occA.layerIndex, new Set());
      if (!marksByLayer.has(occB.layerIndex)) marksByLayer.set(occB.layerIndex, new Set());
      marksByLayer.get(occA.layerIndex).add(occA.pieceIndex);
      marksByLayer.get(occB.layerIndex).add(occB.pieceIndex);
    }
  }

  return marksByLayer;
}

function rotateActivePiece(direction = 1) {
  const step = getRotationStepForCurrentTool();
  const dir = direction < 0 ? -1 : 1;
  state.rotation = snapAngle(state.rotation + dir * step, step);
  rotSelect.value = formatAngleLabel(state.rotation);
  draw();
}

function pieceLayerOrder(type) {
  switch (type) {
    case "Floor2x2":
    case "Floor1x1":
      return 1;
    case "Staircase":
      return 2;
    case "Bed":
      return 2;
    case "Workbench":
      return 2;
    case "Hearth":
      return 2;
    case "FlexiWall":
    case "Wall":
      return 3;
    case "Doorway":
      return 4;
    case "Pillar":
      return 5;
    case "Reserve":
      return 2;
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
  if (piece.type === "FlexiWall") return false;
  const { w, h } = effectiveSize(piece.type, piece.rot);
  return col >= piece.col && col < piece.col + w && row >= piece.row && row < piece.row + h;
}

function getPieceObb(piece) {
  if (piece.type === "Workbench") {
    // Use inner 3×2 bench rectangle: center (col+2, row+2), half-extents (1.5, 1.0)
    return { cx: piece.col + 2, cy: piece.row + 2, hw: 1.5, hh: 1.0, rot: -(piece.rot || 0) * Math.PI / 180 };
  }
  const { w, h } = effectiveSize(piece.type, piece.rot);
  // effectiveSize already handles orthogonal rotation by swapping dims, so rot=0 here
  return { cx: piece.col + w / 2, cy: piece.row + h / 2, hw: w / 2, hh: h / 2, rot: 0 };
}

function obbOverlap(a, b) {
  const axA = { x: Math.cos(a.rot), y: Math.sin(a.rot) };
  const ayA = { x: -Math.sin(a.rot), y: Math.cos(a.rot) };
  const axB = { x: Math.cos(b.rot), y: Math.sin(b.rot) };
  const ayB = { x: -Math.sin(b.rot), y: Math.cos(b.rot) };
  const T = { x: b.cx - a.cx, y: b.cy - a.cy };
  const dot = (u, v) => u.x * v.x + u.y * v.y;
  const projR = (obb, ax, ay, axis) => obb.hw * Math.abs(dot(ax, axis)) + obb.hh * Math.abs(dot(ay, axis));
  for (const axis of [axA, ayA, axB, ayB]) {
    if (Math.abs(dot(T, axis)) >= projR(a, axA, ayA, axis) + projR(b, axB, ayB, axis))
      return false;
  }
  return true;
}

function pieceShowsOrientation(type) {
  return type === "Workbench" || type === "Staircase" || type === "Bed";
}

function drawPieceOrientation(type, rotation, x, y, wPx, hPx, isPreview = false) {
  if (!pieceShowsOrientation(type)) return;

  const cx = x + wPx * 0.5;
  const cy = y + hPx * 0.5;
  const shaftOffset = Math.max(6, Math.min(wPx, hPx) * 0.23);
  const frontInset = Math.max(8, Math.min(wPx, hPx) * 0.18);
  const backInset = Math.max(10, Math.min(wPx, hPx) * 0.28);
  const arrowHead = Math.max(6, Math.min(wPx, hPx) * 0.14);

  ctx.save();
  ctx.translate(cx, cy);
  ctx.rotate((-rotation * Math.PI) / 180);

  const shaftX = 0;
  const backY = -hPx * 0.5 + backInset;
  const frontY = hPx * 0.5 - frontInset;

  if (type === "Bed") {
    // wPx/hPx are effective (swapped) screen-space dims; un-swap them so local
    // drawing axes always match the base 2-wide × 4-tall footprint after the
    // ctx.rotate(-rotation) already applied above.
    const localW = (rotation === 90 || rotation === 270) ? hPx : wPx;
    const localH = (rotation === 90 || rotation === 270) ? wPx : hPx;
    const halfLocalH = localH * 0.5;
    const inset = Math.max(4, localH * 0.1);
    const ah = Math.max(3.5, localW * 0.11);

    const accent = isPreview ? "rgba(38, 43, 23, 0.85)" : "rgba(252, 238, 214, 0.96)";
    ctx.strokeStyle = accent;
    ctx.fillStyle = accent;
    ctx.lineWidth = Math.max(1.5, localW * 0.05);
    ctx.lineCap = "round";

    // Shaft runs along the local long axis (Y), foot→head (toward +Y).
    const from = -halfLocalH + inset * 1.4;
    const to   =  halfLocalH - inset;

    ctx.beginPath();
    ctx.moveTo(0, from);
    ctx.lineTo(0, to - ah * 0.6);
    ctx.stroke();

    // Arrowhead at the head end.
    ctx.beginPath();
    ctx.moveTo(0, to);
    ctx.lineTo(-ah, to - ah * 1.2);
    ctx.lineTo( ah, to - ah * 1.2);
    ctx.closePath();
    ctx.fill();

    ctx.restore();
    return;
  }

  if (type === "Staircase") {
    const accent = isPreview ? "rgba(38, 43, 23, 0.85)" : "rgba(246, 251, 228, 0.96)";
    const subAccent = isPreview ? "rgba(38, 43, 23, 0.62)" : "rgba(246, 251, 228, 0.76)";
    const shaftWidth = Math.max(3, Math.min(wPx, hPx) * 0.075);
    const stepCount = 4;
    const sideInset = Math.max(6, wPx * 0.15);
    const stepLeft = -wPx * 0.5 + sideInset;
    const stepRight = wPx * 0.5 - sideInset;
    const runLength = Math.max(12, frontY - backY);

    // Draw step treads so staircase pieces read differently from plain rectangles.
    ctx.strokeStyle = subAccent;
    ctx.lineWidth = Math.max(2, Math.min(wPx, hPx) * 0.045);
    ctx.lineCap = "round";
    for (let i = 0; i < stepCount; i += 1) {
      const t = (i + 1) / (stepCount + 1);
      const yPos = backY + runLength * t;
      ctx.beginPath();
      ctx.moveTo(stepLeft, yPos);
      ctx.lineTo(stepRight, yPos);
      ctx.stroke();
    }

    // Draw a bold arrow pointing upward travel direction.
    ctx.strokeStyle = accent;
    ctx.fillStyle = accent;
    ctx.lineWidth = shaftWidth;
    ctx.beginPath();
    ctx.moveTo(0, backY + 3);
    ctx.lineTo(0, frontY - arrowHead * 0.55);
    ctx.stroke();

    ctx.beginPath();
    ctx.moveTo(0, frontY);
    ctx.lineTo(-arrowHead * 1.15, frontY - arrowHead * 1.25);
    ctx.lineTo(arrowHead * 1.15, frontY - arrowHead * 1.25);
    ctx.closePath();
    ctx.fill();

    ctx.restore();
    return;
  }

  const cellPx = wPx / 4;
  const rwRect = 3 * cellPx;
  const rhRect = 2 * cellPx;
  ctx.fillStyle = isPreview ? "rgba(20, 184, 166, 0.12)" : "rgba(20, 184, 166, 0.18)";
  ctx.strokeStyle = isPreview ? "rgba(20, 184, 166, 0.6)" : "rgba(20, 184, 166, 0.9)";
  ctx.lineWidth = Math.max(1, cellPx * 0.08);
  ctx.fillRect(-rwRect * 0.5, -rhRect * 0.5, rwRect, rhRect);
  ctx.strokeRect(-rwRect * 0.5, -rhRect * 0.5, rwRect, rhRect);

  ctx.strokeStyle = isPreview ? "rgba(60, 35, 18, 0.78)" : "rgba(255, 245, 220, 0.95)";
  ctx.fillStyle = ctx.strokeStyle;
  ctx.lineWidth = Math.max(2, Math.min(wPx, hPx) * 0.06);
  ctx.lineCap = "round";

  ctx.beginPath();
  ctx.moveTo(shaftX, backY);
  ctx.lineTo(shaftX, frontY);
  ctx.stroke();

  ctx.beginPath();
  ctx.moveTo(shaftX, frontY);
  ctx.lineTo(shaftX - arrowHead, frontY - arrowHead);
  ctx.lineTo(shaftX + arrowHead, frontY - arrowHead);
  ctx.closePath();
  ctx.fill();

  ctx.beginPath();
  ctx.moveTo(shaftX - shaftOffset, backY + arrowHead * 0.4);
  ctx.lineTo(shaftX + shaftOffset, backY + arrowHead * 0.4);
  ctx.stroke();

  ctx.restore();
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

function drawGridCenterLines(originX, originY, gridW, gridH) {
  const cx = originX + gridW * 0.5;
  const cy = originY + gridH * 0.5;
  ctx.save();
  ctx.strokeStyle = "rgba(255, 255, 255, 0.6)";
  ctx.lineWidth = 1;
  ctx.setLineDash([6, 5]);
  ctx.beginPath();
  ctx.moveTo(cx, originY);
  ctx.lineTo(cx, originY + gridH);
  ctx.stroke();
  ctx.beginPath();
  ctx.moveTo(originX, cy);
  ctx.lineTo(originX + gridW, cy);
  ctx.stroke();
  ctx.setLineDash([]);
  ctx.restore();
}

function drawWorkbenchGrid(rotation, x, y, wPx, hPx) {
  const { cell } = gridLayout();
  const cols = Math.round(wPx / cell);
  const rows = Math.round(hPx / cell);
  withPieceRotation(x, y, wPx, hPx, rotation, (rx, ry, rw, rh) => {
    ctx.save();
    ctx.strokeStyle = "rgba(255, 200, 90, 0.45)";
    ctx.lineWidth = 0.5;
    for (let c = 1; c < cols; c++) {
      ctx.beginPath();
      ctx.moveTo(rx + c * cell, ry);
      ctx.lineTo(rx + c * cell, ry + rh);
      ctx.stroke();
    }
    for (let r = 1; r < rows; r++) {
      ctx.beginPath();
      ctx.moveTo(rx, ry + r * cell);
      ctx.lineTo(rx + rw, ry + r * cell);
      ctx.stroke();
    }
    ctx.restore();
  });
}

function withPieceRotation(x, y, w, h, rotation, callback) {
  const normalized = normalizeAngle(rotation || 0);
  const nearestOrth = Math.round(normalized / 90) * 90;
  if (Math.abs(normalized - nearestOrth) < ROTATION_EPSILON) {
    callback(x, y, w, h);
  } else {
    ctx.save();
    ctx.translate(x + w * 0.5, y + h * 0.5);
    ctx.rotate((-normalized * Math.PI) / 180);
    callback(-w * 0.5, -h * 0.5, w, h);
    ctx.restore();
  }
}

const ARC_HANDLE_RADIUS_PX = 9;

function circumcircleFromThreePoints(x1, y1, x2, y2, x3, y3) {
  const ax = x2 - x1, ay = y2 - y1;
  const bx = x3 - x1, by = y3 - y1;
  const D = 2 * (ax * by - ay * bx);
  if (Math.abs(D) < 1e-10) return null;
  const ux = (by * (ax * ax + ay * ay) - ay * (bx * bx + by * by)) / D;
  const uy = (ax * (bx * bx + by * by) - bx * (ax * ax + ay * ay)) / D;
  return { cx: x1 + ux, cy: y1 + uy, r: Math.sqrt(ux * ux + uy * uy) };
}

// Tangent direction of the FlexiWall arc at one of its endpoint cells.
// col/row: the cell whose tangent we want.  otherCol/otherRow: the other endpoint cell.
// mx/my: arc midpoint (grid float coords).  isEnd: true if col/row is the END of the arc.
// Returns {x, y} (not normalised) in the direction the arc is travelling at that endpoint.
function flexiWallEdgeTangent(col, row, otherCol, otherRow, mx, my, isEnd) {
  const thisCX = col + 0.5,  thisCY = row + 0.5;
  const otherCX = otherCol + 0.5, otherCY = otherRow + 0.5;
  const startX = isEnd ? otherCX : thisCX, startY = isEnd ? otherCY : thisCY;
  const endX   = isEnd ? thisCX  : otherCX, endY   = isEnd ? thisCY  : otherCY;
  const circ = circumcircleFromThreePoints(startX, startY, mx, my, endX, endY);
  if (!circ) {
    return { x: endX - startX, y: endY - startY };
  }
  const rx = thisCX - circ.cx, ry = thisCY - circ.cy;
  // Determine traversal direction (CCW vs CW) from start→mx cross start→end
  const crossZ = (mx - startX) * (endY - startY) - (my - startY) * (endX - startX);
  return crossZ >= 0 ? { x: -ry, y: rx } : { x: ry, y: -rx };
}

// Snap an endpoint position to the midpoint of the cell edge the arc enters/exits through.
// tan: forward tangent direction at this endpoint.  isEnd: arc arrives here (true) or departs (false).
function snapToTangentEdge(col, row, tan, isEnd) {
  // START snaps to the BACK edge (opposite of tangent — where the arc came from).
  // END snaps to the FRONT edge (same as tangent — where the arc exits to).
  // Adjacent cells sharing the same boundary therefore produce identical coordinates.
  const dx = isEnd ? tan.x : -tan.x;
  const dy = isEnd ? tan.y : -tan.y;
  if (Math.abs(dx) >= Math.abs(dy)) {
    return dx > 0 ? { x: col + 1, y: row + 0.5 } : { x: col, y: row + 0.5 };
  } else {
    return dy > 0 ? { x: col + 0.5, y: row + 1 } : { x: col + 0.5, y: row };
  }
}

// Recompute snapped x1,y1,x2,y2 for a FlexiWall piece (requires col1/row1/col2/row2/mx/my).
// Coincident endpoints (adjacent cells, near-complete circle) are allowed — drawArcBand handles them.
function applyFlexiWallSnap(p) {
  const t1 = flexiWallEdgeTangent(p.col1, p.row1, p.col2, p.row2, p.mx, p.my, false);
  const t2 = flexiWallEdgeTangent(p.col2, p.row2, p.col1, p.row1, p.mx, p.my, true);
  const s1 = snapToTangentEdge(p.col1, p.row1, t1, false);
  const s2 = snapToTangentEdge(p.col2, p.row2, t2, true);
  p.x1 = s1.x; p.y1 = s1.y;
  p.x2 = s2.x; p.y2 = s2.y;
}

// Derive the cell col/row for a FlexiWall endpoint from its (possibly snapped) coordinate.
// otherX/otherY: the OTHER endpoint's position (used to disambiguate edge midpoints).
// isEnd: whether this is the END point of the arc.
// mx/my: arc midpoint, used as tiebreaker when both endpoints share the same edge coordinate.
function deriveFlexiWallCell(px, py, otherX, otherY, isEnd, mx, my) {
  const xFrac = px - Math.floor(px);
  const yFrac = py - Math.floor(py);
  // Old format: cell centre (.5, .5)
  if (Math.abs(xFrac - 0.5) < 0.01 && Math.abs(yFrac - 0.5) < 0.01)
    return { col: Math.floor(px), row: Math.floor(py) };
  let col = Math.floor(px), row = Math.floor(py);
  if (Math.abs(xFrac) < 0.01 || Math.abs(xFrac - 1) < 0.01) {
    // Vertical edge: x is an integer.
    const xi = Math.round(px);
    if (otherX === px && mx !== undefined) {
      // Both endpoints on same vertical edge — use arc midpoint to decide which side.
      col = mx < px ? xi - 1 : xi;
    } else {
      col = isEnd ? (otherX < px ? xi - 1 : xi) : (otherX > px ? xi : xi - 1);
    }
  }
  if (Math.abs(yFrac) < 0.01 || Math.abs(yFrac - 1) < 0.01) {
    // Horizontal edge: y is an integer.
    const yi = Math.round(py);
    if (otherY === py && my !== undefined) {
      // Both endpoints on same horizontal edge — use arc midpoint to decide which side.
      row = my < py ? yi - 1 : yi;
    } else {
      row = isEnd ? (otherY < py ? yi - 1 : yi) : (otherY > py ? yi : yi - 1);
    }
  }
  return { col, row };
}

function drawArcBand(x1, y1, x2, y2, mx, my, originX, originY, cell) {
  const HALF = 0.5;
  const gx = g => originX + g * cell;
  const gy = g => originY + g * cell;

  // Coincident endpoints: adjacent-cell near-complete circle. Draw a full ring.
  // Centre = midpoint of the boundary point and the midpoint handle (opposite point on circle).
  if (Math.abs(x1 - x2) < 0.02 && Math.abs(y1 - y2) < 0.02) {
    const cx = (x1 + mx) / 2, cy = (y1 + my) / 2;
    const r = Math.sqrt((x1 - cx) ** 2 + (y1 - cy) ** 2);
    if (r < 0.01) return;
    const innerR = Math.max(0.5 * cell, (r - HALF) * cell);
    const outerR = (r + HALF) * cell;
    ctx.beginPath();
    ctx.arc(gx(cx), gy(cy), outerR, 0, 2 * Math.PI);
    ctx.arc(gx(cx), gy(cy), innerR, 0, 2 * Math.PI, true);
    ctx.closePath();
    return;
  }

  const circ = circumcircleFromThreePoints(x1, y1, mx, my, x2, y2);

  if (!circ) {
    const dx = x2 - x1, dy = y2 - y1;
    const len = Math.sqrt(dx * dx + dy * dy);
    if (len < 1e-10) return;
    const nx = (-dy / len) * HALF, ny = (dx / len) * HALF;
    ctx.beginPath();
    ctx.moveTo(gx(x1 + nx), gy(y1 + ny));
    ctx.lineTo(gx(x2 + nx), gy(y2 + ny));
    ctx.lineTo(gx(x2 - nx), gy(y2 - ny));
    ctx.lineTo(gx(x1 - nx), gy(y1 - ny));
    ctx.closePath();
    return;
  }

  const { cx, cy, r } = circ;
  const cxPx = gx(cx), cyPx = gy(cy);
  const startA = Math.atan2(y1 - cy, x1 - cx);
  const endA   = Math.atan2(y2 - cy, x2 - cx);
  const midA   = Math.atan2(my - cy, mx - cx);
  const norm   = a => ((a % (2 * Math.PI)) + 2 * Math.PI) % (2 * Math.PI);
  const s = norm(startA), e = norm(endA), m = norm(midA);
  const cwSpan = ((e - s) + 2 * Math.PI) % (2 * Math.PI);
  const mFromS = ((m - s) + 2 * Math.PI) % (2 * Math.PI);
  const anticlockwise = mFromS > cwSpan;
  const innerR = Math.max(0.5 * cell, (r - HALF) * cell);
  const outerR = (r + HALF) * cell;

  ctx.beginPath();
  ctx.arc(cxPx, cyPx, outerR, startA, endA, anticlockwise);
  ctx.arc(cxPx, cyPx, innerR, endA, startA, !anticlockwise);
  ctx.closePath();
}

function canvasEventToGridFloat(ev) {
  const rect = canvas.getBoundingClientRect();
  const scaleX = canvas.width / rect.width;
  const scaleY = canvas.height / rect.height;
  const { cell, originX, originY } = gridLayout();
  return {
    x: ((ev.clientX - rect.left) * scaleX - originX) / cell,
    y: ((ev.clientY - rect.top)  * scaleY - originY) / cell,
  };
}

function flexiWallHandleHit(ev) {
  const aw = state.flexiWall;
  const rect = canvas.getBoundingClientRect();
  const scaleX = canvas.width / rect.width;
  const scaleY = canvas.height / rect.height;
  const px = (ev.clientX - rect.left) * scaleX;
  const py = (ev.clientY - rect.top)  * scaleY;
  const { cell, originX, originY } = gridLayout();
  const dx = px - (originX + aw.mx * cell);
  const dy = py - (originY + aw.my * cell);
  return Math.sqrt(dx * dx + dy * dy) <= ARC_HANDLE_RADIUS_PX;
}

function findPlacedFlexiWallAtHandle(ev) {
  const rect = canvas.getBoundingClientRect();
  const scaleX = canvas.width / rect.width;
  const scaleY = canvas.height / rect.height;
  const px = (ev.clientX - rect.left) * scaleX;
  const py = (ev.clientY - rect.top)  * scaleY;
  const { cell, originX, originY } = gridLayout();
  for (let i = state.pieces.length - 1; i >= 0; i--) {
    const p = state.pieces[i];
    if (p.type !== "FlexiWall") continue;
    const dx = px - (originX + p.mx * cell);
    const dy = py - (originY + p.my * cell);
    if (Math.sqrt(dx * dx + dy * dy) <= ARC_HANDLE_RADIUS_PX) return i;
  }
  return -1;
}

function cancelFlexiWall() {
  state.flexiWall.phase = "idle";
  state.flexiWall.dragging = false;
  state.flexiWall.dragIndex = -1;
}

function drawFlexiWallPieces(originX, originY, cell) {
  const showHandles = state.currentPiece === "FlexiWall";
  const gx = g => originX + g * cell;
  const gy = g => originY + g * cell;
  for (const p of state.pieces) {
    if (p.type !== "FlexiWall") continue;
    ctx.fillStyle = pieceColors.FlexiWall;
    ctx.globalAlpha = 0.75;
    drawArcBand(p.x1, p.y1, p.x2, p.y2, p.mx, p.my, originX, originY, cell);
    ctx.fill();
    ctx.strokeStyle = "rgba(0,0,0,0.35)";
    ctx.lineWidth = 1;
    ctx.stroke();
    ctx.globalAlpha = 1;
    if (showHandles) {
      ctx.fillStyle = "#ffffff";
      ctx.strokeStyle = pieceColors.FlexiWall;
      ctx.lineWidth = 1.5;
      ctx.globalAlpha = 0.7;
      ctx.beginPath();
      ctx.arc(gx(p.mx), gy(p.my), ARC_HANDLE_RADIUS_PX, 0, 2 * Math.PI);
      ctx.fill();
      ctx.stroke();
      ctx.globalAlpha = 1;
      // Start endpoint marker (green) and end endpoint marker (red) at actual snapped positions
      ctx.globalAlpha = 0.85;
      ctx.fillStyle = "#22c55e";
      ctx.beginPath();
      ctx.arc(gx(p.x1), gy(p.y1), 4, 0, 2 * Math.PI);
      ctx.fill();
      ctx.fillStyle = "#ef4444";
      ctx.beginPath();
      ctx.arc(gx(p.x2), gy(p.y2), 4, 0, 2 * Math.PI);
      ctx.fill();
      ctx.globalAlpha = 1;
    }
  }
}

function drawFlexiWallPreview(originX, originY, cell) {
  const aw = state.flexiWall;
  const gx = g => originX + g * cell;
  const gy = g => originY + g * cell;
  const color = pieceColors.FlexiWall;

  if (aw.phase === "idle") {
    if (state.hover.col < 0) return;
    ctx.fillStyle = color;
    ctx.globalAlpha = 0.55;
    ctx.beginPath();
    ctx.arc(gx(state.hover.col + 0.5), gy(state.hover.row + 0.5), 5, 0, 2 * Math.PI);
    ctx.fill();
    ctx.globalAlpha = 1;
    return;
  }

  if (aw.phase === "placing-end") {
    // Always show the start cell indicator so the first click gives immediate feedback.
    ctx.fillStyle = "#22c55e";
    ctx.globalAlpha = 0.85;
    ctx.beginPath();
    ctx.arc(gx(aw.col1 + 0.5), gy(aw.row1 + 0.5), 5, 0, 2 * Math.PI);
    ctx.fill();
    ctx.globalAlpha = 1;

    if (state.hover.col < 0) return;
    const col2 = state.hover.col, row2 = state.hover.row;
    const pmx = (aw.col1 + 0.5 + col2 + 0.5) / 2, pmy = (aw.row1 + 0.5 + row2 + 0.5) / 2;
    const tmp = { col1: aw.col1, row1: aw.row1, col2, row2, mx: pmx, my: pmy };
    applyFlexiWallSnap(tmp);
    const smx = (tmp.x1 + tmp.x2) / 2, smy = (tmp.y1 + tmp.y2) / 2;
    ctx.fillStyle = color;
    ctx.globalAlpha = 0.35;
    drawArcBand(tmp.x1, tmp.y1, tmp.x2, tmp.y2, smx, smy, originX, originY, cell);
    ctx.fill();
    ctx.globalAlpha = 1;
    ctx.fillStyle = color;
    ctx.beginPath();
    ctx.arc(gx(tmp.x1), gy(tmp.y1), 5, 0, 2 * Math.PI);
    ctx.fill();
    return;
  }

}

function draw() {
  const { cell, gridW, gridH, originX, originY } = gridLayout();
  const { topOverlappingPieceIndexes } = getOverlapInfo();
  const crossLayerOverlapMarks = getCrossLayerOverlapInfo();
  const activeCrossLayerOverlapMarks = crossLayerOverlapMarks.get(state.activeLayoutIndex) || new Set();

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

  const orderedPieces = state.pieces
    .map((piece, index) => ({ piece, index }))
    .sort((a, b) => pieceLayerOrder(a.piece.type) - pieceLayerOrder(b.piece.type));

  for (const entry of orderedPieces) {
    const p = entry.piece;
    if (p.type === "FlexiWall") continue;
    const { w, h } = effectiveSize(p.type, p.rot);
    const x = originX + p.col * cell;
    const y = originY + p.row * cell;
    ctx.fillStyle = pieceColors[p.type] || "#999";
    ctx.globalAlpha = 0.75;
    withPieceRotation(x + 1, y + 1, w * cell - 2, h * cell - 2, p.rot, (rx, ry, rw, rh) => {
      ctx.fillRect(rx, ry, rw, rh);
      ctx.strokeStyle = "rgba(0, 0, 0, 0.35)";
      ctx.strokeRect(rx, ry, rw, rh);
    });
    if (p.type === "Workbench") drawWorkbenchGrid(p.rot || 0, x + 1, y + 1, w * cell - 2, h * cell - 2);
    drawPieceOrientation(p.type, p.rot || 0, x + 1, y + 1, w * cell - 2, h * cell - 2);
    ctx.globalAlpha = 1;

    if (activeCrossLayerOverlapMarks.has(entry.index)) {
      ctx.strokeStyle = "#ff1f1f";
      ctx.lineWidth = 2.5;
      ctx.setLineDash([5, 3]);
      withPieceRotation(x + 1, y + 1, w * cell - 2, h * cell - 2, p.rot, (rx, ry, rw, rh) => {
        ctx.strokeRect(rx, ry, rw, rh);
      });
      ctx.setLineDash([]);
    }
  }

  // Draw optional non-active level overlays above the active layout so they are always visible.
  for (let levelIndex = 0; levelIndex < state.layouts.length; levelIndex += 1) {
    if (levelIndex === state.activeLayoutIndex) continue;
    if (!state.overlayEnabled[levelIndex]) continue;

    const overlayLayout = state.layouts[levelIndex];
    const overlayCrossLayerMarks = crossLayerOverlapMarks.get(levelIndex) || new Set();
    const overlayPieces = overlayLayout.pieces
      .map((piece, index) => ({ piece, index }))
      .sort((a, b) => pieceLayerOrder(a.piece.type) - pieceLayerOrder(b.piece.type));
    const strokeColor = levelOverlayStrokeColors[levelIndex] || "#ffffff";
    for (const entry of overlayPieces) {
      const p = entry.piece;
      if (p.type === "FlexiWall") continue;
      const { w, h } = effectiveSize(p.type, p.rot);
      const x = originX + p.col * cell;
      const y = originY + p.row * cell;
      ctx.fillStyle = pieceColors[p.type] || "#999";
      ctx.globalAlpha = 0.22;
      withPieceRotation(x + 1, y + 1, w * cell - 2, h * cell - 2, p.rot, (rx, ry, rw, rh) => {
        ctx.fillRect(rx, ry, rw, rh);
      });
      ctx.globalAlpha = 0.7;
      ctx.lineWidth = 1.25;
      ctx.strokeStyle = strokeColor;
      withPieceRotation(x + 1, y + 1, w * cell - 2, h * cell - 2, p.rot, (rx, ry, rw, rh) => {
        ctx.strokeRect(rx, ry, rw, rh);
      });

      if (overlayCrossLayerMarks.has(entry.index)) {
        ctx.globalAlpha = 0.95;
        ctx.lineWidth = 2.5;
        ctx.strokeStyle = "#ff1f1f";
        ctx.setLineDash([5, 3]);
        withPieceRotation(x + 1, y + 1, w * cell - 2, h * cell - 2, p.rot, (rx, ry, rw, rh) => {
          ctx.strokeRect(rx, ry, rw, rh);
        });
        ctx.setLineDash([]);
      }

      ctx.globalAlpha = 0.55;
      if (p.type === "Workbench") drawWorkbenchGrid(p.rot || 0, x + 1, y + 1, w * cell - 2, h * cell - 2);
      drawPieceOrientation(p.type, p.rot || 0, x + 1, y + 1, w * cell - 2, h * cell - 2, true);
      ctx.globalAlpha = 1;
    }
  }

  for (const pieceIndex of topOverlappingPieceIndexes) {
    const p = state.pieces[pieceIndex];
    const { w, h } = effectiveSize(p.type, p.rot);
    const x = originX + p.col * cell;
    const y = originY + p.row * cell;
    ctx.strokeStyle = "#ff7a7a";
    ctx.lineWidth = 3;
    ctx.setLineDash([8, 5]);
    withPieceRotation(x + 1, y + 1, w * cell - 2, h * cell - 2, p.rot, (rx, ry, rw, rh) => {
      ctx.strokeRect(rx, ry, rw, rh);
    });
  }
  ctx.setLineDash([]);

  if (state.currentPiece !== "FlexiWall" && state.hover.col >= 0 && state.hover.row >= 0) {
    const { w, h } = effectiveSize(state.currentPiece, state.rotation);
    const x = originX + state.hover.col * cell;
    const y = originY + state.hover.row * cell;
    const wPx = w * cell;
    const hPx = h * cell;
    const hoverBase = pieceColors[state.currentPiece] || "#999999";
    const hoverTint = lightenHexColor(hoverBase, 0.6);
    ctx.fillStyle = `rgba(${hoverTint.r}, ${hoverTint.g}, ${hoverTint.b}, 0.38)`;

    const normalizedRotation = normalizeAngle(state.rotation);
    const nearestOrth = Math.round(normalizedRotation / 90) * 90;
    const isOrthogonal = Math.abs(normalizedRotation - nearestOrth) < ROTATION_EPSILON;

    if (isOrthogonal) {
      ctx.fillRect(x, y, wPx, hPx);
    } else {
      const cx = x + wPx * 0.5;
      const cy = y + hPx * 0.5;
      ctx.save();
      ctx.translate(cx, cy);
      ctx.rotate((-state.rotation * Math.PI) / 180);
      ctx.fillRect(-wPx * 0.5, -hPx * 0.5, wPx, hPx);
      ctx.restore();
    }

    drawPieceOrientation(state.currentPiece, state.rotation, x, y, wPx, hPx, true);
  }

  drawFlexiWallPieces(originX, originY, cell);
  drawFlexiWallPreview(originX, originY, cell);
  drawGridCenterLines(originX, originY, gridW, gridH);
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

    if (line.startsWith("flexiwall,") || line.startsWith("arcwall,")) {
      const parts = line.split(",");
      if (parts.length < 7) continue;
      const [, x1, y1, x2, y2, mx, my] = parts.map(Number);
      if ([x1, y1, x2, y2, mx, my].some(v => !Number.isFinite(v))) continue;
      const c1 = deriveFlexiWallCell(x1, y1, x2, y2, false, mx, my);
      const c2 = deriveFlexiWallCell(x2, y2, x1, y1, true, mx, my);
      const p = { type: "FlexiWall", col1: c1.col, row1: c1.row, col2: c2.col, row2: c2.row,
                  x1, y1, x2, y2, mx, my };
      applyFlexiWallSnap(p);
      next.pieces.push(p);
    }
  }

  return next;
}

function serializeVfp() {
  const lines = [`cols=${state.cols}`, `rows=${state.rows}`];
  for (const p of state.pieces) {
    if (p.type === "FlexiWall") {
      const fmt = v => Math.round(v * 1000) / 1000;
      lines.push(`flexiwall,${fmt(p.x1)},${fmt(p.y1)},${fmt(p.x2)},${fmt(p.y2)},${fmt(p.mx)},${fmt(p.my)}`);
    } else {
      lines.push(`piece,${p.col},${p.row},${p.type},${p.rot || 0}`);
    }
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
  const levelLabel = `Level ${state.activeLayoutIndex + 1}`;
  return window.confirm(`${levelLabel} has unsaved changes. Continue with ${actionName}?`);
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
  if (state.currentPiece === "FlexiWall") {
    const aw = state.flexiWall;
    if (aw.dragging && aw.dragIndex >= 0) {
      const { x, y } = canvasEventToGridFloat(ev);
      const p = state.pieces[aw.dragIndex];
      p.mx = x; p.my = y;
      if (p.col1 !== undefined) applyFlexiWallSnap(p);
      aw.dragMoved = true;
    } else {
      const { col, row } = canvasEventToGridCell(ev);
      state.hover.col = (col >= 0 && col < state.cols) ? col : -1;
      state.hover.row = (row >= 0 && row < state.rows) ? row : -1;
    }
    draw();
    return;
  }
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

canvas.addEventListener("mousedown", (ev) => {
  if (state.currentPiece !== "FlexiWall") return;
  const aw = state.flexiWall;
  if (aw.phase === "idle") {
    const idx = findPlacedFlexiWallAtHandle(ev);
    if (idx >= 0) {
      aw.dragging = true;
      aw.dragIndex = idx;
      aw.dragMoved = false;
      ev.preventDefault();
    }
  }
});

canvas.addEventListener("mouseup", () => {
  if (state.currentPiece !== "FlexiWall") return;
  const aw = state.flexiWall;
  if (aw.dragging) {
    const moved = aw.dragMoved;
    aw.dragging = false;
    aw.dragIndex = -1;
    aw.dragMoved = false;
    if (moved) {
      aw.dragJustEnded = true;
      markDirty(true);
    }
  }
});

canvas.addEventListener("click", (ev) => {
  if (state.currentPiece === "FlexiWall") {
    const aw = state.flexiWall;
    if (aw.dragJustEnded) {
      aw.dragJustEnded = false;
      return;
    }
    if (aw.phase === "idle") {
      if (state.hover.col < 0) return;
      aw.col1 = state.hover.col;
      aw.row1 = state.hover.row;
      aw.phase = "placing-end";
    } else if (aw.phase === "placing-end") {
      if (state.hover.col < 0) return;
      const col2 = state.hover.col, row2 = state.hover.row;
      if (aw.col1 === col2 && aw.row1 === row2) return;
      const pmx = (aw.col1 + 0.5 + col2 + 0.5) / 2, pmy = (aw.row1 + 0.5 + row2 + 0.5) / 2;
      const newPiece = { type: "FlexiWall", col1: aw.col1, row1: aw.row1, col2, row2,
                         x1: 0, y1: 0, x2: 0, y2: 0, mx: pmx, my: pmy };
      applyFlexiWallSnap(newPiece);
      newPiece.mx = (newPiece.x1 + newPiece.x2) / 2;
      newPiece.my = (newPiece.y1 + newPiece.y2) / 2;
      state.pieces.push(newPiece);
      markDirty(true);
      aw.phase = "idle";
    }
    draw();
    return;
  }
  if (state.hover.col < 0 || state.hover.row < 0) return;
  addAt(state.hover.col, state.hover.row);
  draw();
});

canvas.addEventListener("contextmenu", (ev) => {
  ev.preventDefault();
  if (state.currentPiece === "FlexiWall") {
    const idx = findPlacedFlexiWallAtHandle(ev);
    if (idx >= 0) {
      state.pieces.splice(idx, 1);
      markDirty(true);
    } else {
      cancelFlexiWall();
    }
    draw();
    return;
  }
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

  markDirty(true);
  draw();
}

function cmdOneDoor() {
  state.pieces = [];

  const cols = state.cols;
  const rows = state.rows;

  // 1. Fill with Floor2x2 tiles
  for (let r = 0; r <= rows - 2; r += 2) {
    for (let c = 0; c <= cols - 2; c += 2) {
      state.pieces.push({ col: c, row: r, type: "Floor2x2", rot: 0 });
    }
  }

  // 2. Single doorway centered on the bottom (front) edge
  const doorCol = Math.floor((cols - 2) / 2);
  const door = { col: doorCol, row: rows - 1, type: "Doorway", rot: 0 };
  state.pieces.push(door);

  function wallOverlapsDoor(col, row, rot) {
    const ws = effectiveSize("Wall", rot);
    const ds = effectiveSize("Doorway", door.rot);
    return col < door.col + ds.w && col + ws.w > door.col &&
           row < door.row + ds.h && row + ws.h > door.row;
  }

  // 3. Bottom (front) edge walls (rot 0), skipping doorway
  for (let c = 0; c <= cols - 2; c += 2) {
    if (wallOverlapsDoor(c, rows - 1, 0)) continue;
    state.pieces.push({ col: c, row: rows - 1, type: "Wall", rot: 0 });
  }

  // 4. Top (back) edge walls (rot 180)
  for (let c = 0; c <= cols - 2; c += 2) {
    state.pieces.push({ col: c, row: 0, type: "Wall", rot: 180 });
  }

  // 5. Left edge walls (rot 270 = outer face left)
  for (let r = 0; r <= rows - 2; r += 2) {
    state.pieces.push({ col: 0, row: r, type: "Wall", rot: 270 });
  }

  // 6. Right edge walls (rot 90 = outer face right)
  for (let r = 0; r <= rows - 2; r += 2) {
    state.pieces.push({ col: cols - 1, row: r, type: "Wall", rot: 90 });
  }

  // 7. Pillars flanking the doorway
  if (doorCol - 1 >= 0)        state.pieces.push({ col: doorCol - 1, row: rows - 1, type: "Pillar", rot: 0 });
  if (doorCol + 2 <= cols - 1) state.pieces.push({ col: doorCol + 2, row: rows - 1, type: "Pillar", rot: 0 });


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
document.getElementById("oneDoorBtn").addEventListener("click", cmdOneDoor);

for (const btn of levelButtons) {
  btn.addEventListener("click", () => {
    const index = parseInt(btn.getAttribute("data-level-index"), 10);
    if (!Number.isInteger(index)) return;
    switchLayout(index);
  });
}

for (const cb of overlayCheckboxes) {
  cb.addEventListener("change", () => {
    const index = parseInt(cb.getAttribute("data-overlay-index"), 10);
    if (!Number.isInteger(index)) return;
    if (index === state.activeLayoutIndex) {
      cb.checked = false;
      return;
    }

    state.overlayEnabled[index] = cb.checked;
    draw();
  });
}

for (const radio of pieceTypeRadios) {
  radio.addEventListener("change", () => {
    if (!radio.checked) return;
    if (state.currentPiece === "FlexiWall") cancelFlexiWall();
    state.currentPiece = radio.value;
    syncRotationControlsForCurrentTool();
    draw();
  });
}

rotSelect.addEventListener("change", () => {
  state.rotation = snapAngle(Number.parseFloat(rotSelect.value) || 0, getRotationStepForCurrentTool());
  rotSelect.value = formatAngleLabel(state.rotation);
  draw();
});

canvas.addEventListener("wheel", (ev) => {
  ev.preventDefault();
  const direction = ev.deltaY > 0 ? 1 : -1;
  rotateActivePiece(direction);
}, { passive: false });

window.addEventListener("keydown", (ev) => {
  if (ev.key === "Escape" && state.currentPiece === "FlexiWall") {
    cancelFlexiWall();
    draw();
    return;
  }

  if (ev.altKey && !ev.shiftKey && !ev.ctrlKey && !ev.metaKey) {
    if (ev.key === "1" || ev.key === "2" || ev.key === "3") {
      ev.preventDefault();
      switchLayout(parseInt(ev.key, 10) - 1);
      return;
    }
  }

  const isArrow = ev.key === "ArrowRight" || ev.key === "ArrowUp" || ev.key === "ArrowLeft" || ev.key === "ArrowDown";
  if (!isArrow) return;

  // Canvas-hover takes precedence: rotate piece even if a radio keeps keyboard focus.
  if (state.isCanvasHovered) {
    ev.preventDefault();
    if (ev.key === "ArrowRight" || ev.key === "ArrowUp") {
      rotateActivePiece(1);
    } else {
      rotateActivePiece(-1);
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
  persistActiveLayout();
  const hasUnsaved = state.layouts.some((layout) => layout.dirty);
  if (!hasUnsaved) return;
  ev.preventDefault();
  ev.returnValue = "";
});

initPieceSwatches();
syncRotationControlsForCurrentTool();
loadLayoutIntoState(0);
updateLevelButtons();
draw();
markDirty(false);
