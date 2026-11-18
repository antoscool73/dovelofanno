// Righe orizzontali di locandine: con il mouse si scorrono trascinando (al tocco lo scorrimento è già quello nativo).
// Un trascinamento oltre 6 px non apre la locandina su cui è finito il mouse.
export function dragScroll(node: HTMLElement) {
  let startX = 0;
  let startLeft = 0;
  let down = false;
  let moved = false;
  let pointer = -1;

  function onDown(e: PointerEvent) {
    if (e.pointerType !== 'mouse' || e.button !== 0 || node.scrollWidth <= node.clientWidth) return;
    down = true;
    moved = false;
    startX = e.clientX;
    startLeft = node.scrollLeft;
    pointer = e.pointerId;
  }
  function onMove(e: PointerEvent) {
    if (!down) return;
    const dx = e.clientX - startX;
    if (!moved && Math.abs(dx) > 6) {
      moved = true;
      node.setPointerCapture(pointer);
      node.classList.add('dragging');
    }
    if (moved) {
      node.scrollLeft = startLeft - dx;
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
