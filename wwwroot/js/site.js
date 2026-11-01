// Deadline reminders: polls the server once a minute, updates the bell,
// and shows a desktop notification for items that are about to start (if allowed in Settings).
(function () {
  var bell = document.getElementById('bell');
  var panel = document.getElementById('bell-panel');
  var count = document.getElementById('bell-count');
  var list = document.getElementById('bell-list');
  var empty = document.getElementById('bell-empty');
  if (!bell) return;

  bell.addEventListener('click', function () {
    var open = panel.hidden;
    panel.hidden = !open;
    bell.setAttribute('aria-expanded', String(open));
    if (open && 'Notification' in window && Notification.permission === 'default') {
      Notification.requestPermission();
    }
  });
  document.addEventListener('click', function (e) {
    if (!panel.hidden && !panel.contains(e.target) && !bell.contains(e.target)) {
      panel.hidden = true; bell.setAttribute('aria-expanded', 'false');
    }
  });

  function alreadyNotified(id) {
    try {
      var seen = JSON.parse(sessionStorage.getItem('fp-notified') || '[]');
      if (seen.indexOf(id) !== -1) return true;
      seen.push(id); sessionStorage.setItem('fp-notified', JSON.stringify(seen));
    } catch (e) {}
    return false;
  }

  function refresh() {
    fetch('/Tasks/Reminders', { headers: { 'Accept': 'application/json' }, credentials: 'same-origin' })
      .then(function (r) { return r.ok ? r.json() : { desktop: false, items: [] }; })
      .then(function (data) {
        var items = data.items || [];
        list.innerHTML = '';
        items.forEach(function (t) {
          var li = document.createElement('li');
          if (t.overdue) li.className = 'over';
          var a = document.createElement('a');
          a.href = '/Tasks/Edit/' + t.id; a.textContent = t.title;
          var s = document.createElement('div');
          s.className = 'small muted'; s.textContent = t.message;
          li.appendChild(a); li.appendChild(s); list.appendChild(li);

          if (data.desktop && t.startsSoon && 'Notification' in window &&
              Notification.permission === 'granted' && !alreadyNotified(t.id)) {
            new Notification('FlexPlan: ' + t.title, { body: t.message });
          }
        });
        empty.hidden = items.length > 0;
        count.hidden = items.length === 0;
        count.textContent = items.length;
      })
      .catch(function () {});
  }

  refresh();
  setInterval(refresh, 60000);
})();
