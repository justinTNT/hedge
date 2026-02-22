(function () {
  var KEY = 'hedge_guest_session';
  var adj = ['Brave','Calm','Clever','Daring','Eager','Fierce','Gentle','Happy',
    'Keen','Lively','Merry','Noble','Proud','Quick','Sharp','Swift',
    'Tall','Warm','Wild','Wise','Bold','Bright','Cool','Fair'];
  var ani = ['Falcon','Otter','Panda','Tiger','Eagle','Whale','Raven','Fox',
    'Wolf','Bear','Crane','Hawk','Lynx','Owl','Seal','Stag',
    'Wren','Hare','Ibis','Jay','Kite','Lark','Newt','Pike'];
  function pick(a) { return a[Math.floor(Math.random() * a.length)]; }
  function getSession() {
    var s = localStorage.getItem(KEY);
    if (s) { try { return JSON.parse(s); } catch(_) {} }
    var n = { guestId: 'guest-' + Math.random().toString(36).substring(2,10),
              displayName: pick(adj) + ' ' + pick(ani),
              createdAt: Math.floor(Date.now()/1000) };
    localStorage.setItem(KEY, JSON.stringify(n));
    return n;
  }
  window.HedgeGuest = { getSession: getSession };
})();
