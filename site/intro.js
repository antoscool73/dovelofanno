// Intro col jingle sul sito (23/11/2026). I browser lasciano partire da solo solo un video muto: lo si fa partire quando
// entra nello schermo; alla fine torna all'immagine del logo (intro.jpg, ricaricando il video: va anche senza Range).
// Il pulsante lo fa rivedere da capo con l'audio.
(function () {
  var v = document.getElementById('intro');
  var b = document.getElementById('intro-audio');
  if (!v || !b) return;
  var calm = window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  var started = false;
  v.addEventListener('ended', function () { v.muted = true; v.load(); });
  if (!calm && 'IntersectionObserver' in window) {
    new IntersectionObserver(function (es, o) {
      if (es[0].isIntersecting && !started) { started = true; o.disconnect(); v.play().catch(function () {}); }
    }, { threshold: 0.5 }).observe(v);
  }
  b.addEventListener('click', function () {
    started = true;
    v.load(); // da capo
    v.muted = false;
    v.volume = 0.8;
    v.play().catch(function () {});
  });
})();
