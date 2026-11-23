// Righe orizzontali di locandine: con il mouse si scorrono trascinando (al tocco lo scorrimento è già quello nativo).
// Un trascinamento oltre 6 px non apre la locandina su cui è finito il mouse. Con { vertical: true } (la griglia della
// guida, 23/11/2026) si trascina anche in verticale.
export function dragScroll(node: HTMLElement, opts: { vertical?: boolean } = {}) {
  let startX = 0;
  let startY = 0;
  let startLeft = 0;
  let startTop = 0;
  let down = false;
  let moved = false;
  let pointer = -1;

  function onDown(e: PointerEvent) {
    const canX = node.scrollWidth > node.clientWidth;
    const canY = !!opts.vertical && node.scrollHeight > node.clientHeight;
    if (e.pointerType !== 'mouse' || e.button !== 0 || (!canX && !canY)) return;
    // non sui controlli dove si scrive o si sceglie
    if ((e.target as HTMLElement).closest('input, select, textarea')) return;
    down = true;
    moved = false;
    startX = e.clientX;
    startY = e.clientY;
    startLeft = node.scrollLeft;
    startTop = node.scrollTop;
    pointer = e.pointerId;
  }
  function onMove(e: PointerEvent) {
    if (!down) return;
    const dx = e.clientX - startX;
    const dy = opts.vertical ? e.clientY - startY : 0;
    if (!moved && Math.hypot(dx, dy) > 6) {
      moved = true;
      node.setPointerCapture(pointer);
      node.classList.add('dragging');
    }
    if (moved) {
      node.scrollLeft = startLeft - dx;
      if (opts.vertical) node.scrollTop = startTop - dy;
      e.preventDefault();
    }
  }
  function onUp() {
    if (!down) return;
    down = false;
    if (moved) {
      node.classList.remove('dragging');
      try { node.releasePointerCapture(pointer); } catch { /* già rilasciato */ }
    }
  }
  // dopo un trascinamento il clic finale non apre niente
  function onClick(e: MouseEvent) {
    if (!moved) return;
    e.preventDefault();
    e.stopPropagation();
    moved = false;
  }
  const noDrag = (e: DragEvent) => e.preventDefault();

  node.addEventListener('pointerdown', onDown);
  node.addEventListener('pointermove', onMove);
  node.addEventListener('pointerup', onUp);
  node.addEventListener('pointercancel', onUp);
  node.addEventListener('click', onClick, true);
  node.addEventListener('dragstart', noDrag);
  return {
    update(o: { vertical?: boolean } = {}) { opts = o; },
    destroy() {
      node.removeEventListener('pointerdown', onDown);
      node.removeEventListener('pointermove', onMove);
      node.removeEventListener('pointerup', onUp);
      node.removeEventListener('pointercancel', onUp);
      node.removeEventListener('click', onClick, true);
      node.removeEventListener('dragstart', noDrag);
    },
  };
}
