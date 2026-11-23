// Frecce del carosello delle schermate (23/11/2026, richiesta di Antonio al posto del trascinamento col mouse).
// Solo un aiuto in più: il contenuto è tutto nell'HTML e senza questo script il carosello si scorre come prima.
// Ogni clic porta all'inizio della schermata dopo (o prima), così l'aggancio dello scorrimento resta allineato;
// all'inizio e alla fine la freccia di quel lato si spegne. Caricato con defer: non ferma il disegno della pagina.
(function () {
  'use strict';
  var rows = document.querySelectorAll('.h-row');
  Array.prototype.forEach.call(rows, function (row) {
    var car = document.createElement('div');
    car.className = 'h-car';
    row.parentNode.insertBefore(car, row);
    car.appendChild(row);

    function arrow(cls, label, text, dir) {
      var b = document.createElement('button');
      b.type = 'button';
      b.className = 'h-arrow ' + cls;
      b.setAttribute('aria-label', label);
      b.textContent = text;
      b.addEventListener('click', function () {
        // la schermata successiva (o precedente) rispetto a quella allineata a sinistra: si va al suo inizio, che è
        // anche il punto di aggancio, così due clic di fila non si annullano a metà dello scorrimento morbido
        var base = row.getBoundingClientRect().left - row.scrollLeft;
        var starts = Array.prototype.map.call(row.children, function (el) {
          return el.getBoundingClientRect().left - base;
        });
        var at = target == null ? row.scrollLeft : target;
        var to = null;
        if (dir > 0) { for (var i = 0; i < starts.length; i++) if (starts[i] > at + 8) { to = starts[i]; break; } }
        else { for (var j = starts.length - 1; j >= 0; j--) if (starts[j] < at - 8) { to = starts[j]; break; } }
        if (to == null) to = dir > 0 ? row.scrollWidth : 0;
        target = Math.max(0, Math.min(to, row.scrollWidth - row.clientWidth));
        row.scrollTo({ left: target, behavior: 'smooth' });
      });
      car.appendChild(b);
      return b;
    }
    var prev = arrow('prev', 'Schermata precedente', '‹', -1);
    var next = arrow('next', 'Schermata successiva', '›', 1);

    // dove sta andando lo scorrimento morbido partito da una freccia (null = fermo): un secondo clic parte da lì
    var target = null, idle = 0;
    row.addEventListener('scroll', function () {
      clearTimeout(idle);
      idle = setTimeout(function () { target = null; }, 150);
    }, { passive: true });

    function update() {
      var max = row.scrollWidth - row.clientWidth - 8;   // 8 px: l'aggancio lascia qualche pixel ai bordi
      prev.disabled = row.scrollLeft <= 8;
      next.disabled = row.scrollLeft >= max;
    }
    row.addEventListener('scroll', update, { passive: true });
    window.addEventListener('resize', update);
    update();
  });
})();
