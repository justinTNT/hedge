// The framework's shared guest-session runtime (defines window.HedgeGuest, the
// partner of Hedge's Client.GuestSession). One source: apps copy it into lib/ at
// prep time (see each app's prep:lib script); the per-app lib/guest-session.js is
// generated + gitignored. Do not edit a copy. (music carries its own slimmer,
// divergent version — no identity/syncSession/avatarForAuthor.)
(function () {
  var KEY = 'hedge_guest_session';
  var adjectives = ['Sleepy','Brave','Grumpy','Neon','Ancient','Quantum','Wandering','Clever',
    'Daring','Gentle','Happy','Keen','Lively','Merry','Noble','Proud',
    'Quick','Sharp','Swift','Tall','Warm','Wild','Wise','Bold',
    'Bright','Cool','Fair','Calm','Fierce','Eager','Sunny','Lucky',
    'Jolly','Silly','Chilly','Cosmic','Mystic','Lunar','Solar','Stellar',
    'Astral','Galactic','Epic','Heroic','Magic','Secret','Hidden','Lost',
    'Found','Quiet','Loud','Fuzzy','Spiky','Smooth','Rough','Soft',
    'Hard','Sweet','Sour','Spicy','Salty','Bitter','Fresh','Stale',
    'Crisp','Crunchy','Chewy','Sticky','Slippery','Shiny','Dull','Dark',
    'Light','Heavy','Empty','Full','Hollow','Solid','Liquid','Gas',
    'Hot','Cold','Freezing','Boiling','Fast','Slow','Sluggish','Rapid',
    'Leisurely','Hasty','Deliberate','Young','Old','New','Modern','Classic',
    'Vintage','Retro','Tiny','Small','Medium','Large','Huge','Giant',
    'Massive','Colossal','Good','Bad','Great','Terrible','Excellent','Awful',
    'Wonderful','Horrible','Sad','Joyful','Sorrowful','Glad','Upset','Cheerful',
    'Miserable','Angry','Furious','Peaceful','Mad','Tranquil','Enraged','Serene',
    'Cowardly','Courageous','Fearful','Fearless','Timid','Afraid','Smart','Stupid',
    'Foolish','Intelligent','Ignorant','Unwise','Rich','Poor','Wealthy','Impoverished',
    'Affluent','Destitute','Prosperous','Needy','Beautiful','Ugly','Gorgeous','Hideous',
    'Attractive','Unattractive','Handsome','Plain','Clean','Dirty','Spotless','Filthy',
    'Immaculate','Grubby','Pristine','Messy','Dry','Wet','Arid','Damp',
    'Parched','Moist','Dehydrated','Soaked'];
  var colors = [
    {name:'Slate',hex:'#64748b'},{name:'Gray',hex:'#6b7280'},{name:'Zinc',hex:'#71717a'},
    {name:'Neutral',hex:'#737373'},{name:'Stone',hex:'#78716c'},{name:'Red',hex:'#ef4444'},
    {name:'Orange',hex:'#f97316'},{name:'Amber',hex:'#f59e0b'},{name:'Yellow',hex:'#eab308'},
    {name:'Lime',hex:'#84cc16'},{name:'Green',hex:'#22c55e'},{name:'Emerald',hex:'#10b981'},
    {name:'Teal',hex:'#14b8a6'},{name:'Cyan',hex:'#06b6d4'},{name:'Sky',hex:'#0ea5e9'},
    {name:'Blue',hex:'#3b82f6'},{name:'Indigo',hex:'#6366f1'},{name:'Violet',hex:'#8b5cf6'},
    {name:'Purple',hex:'#a855f7'},{name:'Fuchsia',hex:'#d946ef'},{name:'Pink',hex:'#ec4899'},
    {name:'Rose',hex:'#f43f5e'},{name:'Coral',hex:'#ff7f50'},{name:'Salmon',hex:'#fa8072'},
    {name:'Tomato',hex:'#ff6347'},{name:'Gold',hex:'#ffd700'},{name:'Olive',hex:'#808000'},
    {name:'Navy',hex:'#000080'},{name:'Maroon',hex:'#800000'},{name:'Plum',hex:'#dda0dd'}];
  var emojis = [
    {c:'🐒',n:'Monkey'},{c:'🦍',n:'Gorilla'},{c:'🐕',n:'Dog'},{c:'🐩',n:'Poodle'},
    {c:'🐺',n:'Wolf'},{c:'🦊',n:'Fox'},{c:'🐈',n:'Cat'},{c:'🦁',n:'Lion'},
    {c:'🐅',n:'Tiger'},{c:'🐆',n:'Leopard'},{c:'🐎',n:'Horse'},{c:'🦄',n:'Unicorn'},
    {c:'🦓',n:'Zebra'},{c:'🦌',n:'Deer'},{c:'🐄',n:'Cow'},{c:'🐂',n:'Ox'},
    {c:'🐃',n:'Buffalo'},{c:'🐖',n:'Pig'},{c:'🐗',n:'Boar'},{c:'🐏',n:'Ram'},
    {c:'🐑',n:'Sheep'},{c:'🐐',n:'Goat'},{c:'🐪',n:'Camel'},{c:'🦙',n:'Llama'},
    {c:'🦒',n:'Giraffe'},{c:'🐘',n:'Elephant'},{c:'🦏',n:'Rhino'},{c:'🦛',n:'Hippo'},
    {c:'🐁',n:'Mouse'},{c:'🐀',n:'Rat'},{c:'🐹',n:'Hamster'},{c:'🐇',n:'Rabbit'},
    {c:'🐿️',n:'Chipmunk'},{c:'🦔',n:'Hedgehog'},{c:'🦇',n:'Bat'},{c:'🐻',n:'Bear'},
    {c:'🐨',n:'Koala'},{c:'🐼',n:'Panda'},{c:'🦥',n:'Sloth'},{c:'🦦',n:'Otter'},
    {c:'🦨',n:'Skunk'},{c:'🦘',n:'Kangaroo'},{c:'🦡',n:'Badger'},{c:'🦃',n:'Turkey'},
    {c:'🐔',n:'Hen'},{c:'🐓',n:'Rooster'},{c:'🐦',n:'Bird'},{c:'🐧',n:'Penguin'},
    {c:'🕊️',n:'Dove'},{c:'🦅',n:'Eagle'},{c:'🦆',n:'Duck'},{c:'🦢',n:'Swan'},
    {c:'🦉',n:'Owl'},{c:'🦩',n:'Flamingo'},{c:'🦚',n:'Peacock'},{c:'🦜',n:'Parrot'},
    {c:'🐸',n:'Frog'},{c:'🐊',n:'Croc'},{c:'🐢',n:'Turtle'},{c:'🦎',n:'Lizard'},
    {c:'🐍',n:'Snake'},{c:'🐉',n:'Dragon'},{c:'🦕',n:'Dino'},{c:'🦖',n:'Rex'},
    {c:'🐋',n:'Whale'},{c:'🐬',n:'Dolphin'},{c:'🦭',n:'Seal'},{c:'🐟',n:'Fish'},
    {c:'🐡',n:'Puffer'},{c:'🦈',n:'Shark'},{c:'🐙',n:'Octopus'},{c:'🐌',n:'Snail'},
    {c:'🦋',n:'Butterfly'},{c:'🐛',n:'Bug'},{c:'🐜',n:'Ant'},{c:'🐝',n:'Bee'},
    {c:'🪲',n:'Beetle'},{c:'🐞',n:'Ladybug'},{c:'🦗',n:'Cricket'},{c:'🕷️',n:'Spider'},
    {c:'🦂',n:'Scorpion'},{c:'🦟',n:'Mosquito'},{c:'🪰',n:'Fly'},{c:'🪱',n:'Worm'},
    {c:'🦠',n:'Microbe'},{c:'💐',n:'Bouquet'},{c:'🌸',n:'Blossom'},{c:'💮',n:'Flower'},
    {c:'🏵️',n:'Rosette'},{c:'🌹',n:'Rose'},{c:'🥀',n:'Wilt'},{c:'🌺',n:'Hibiscus'},
    {c:'🌻',n:'Sunflower'},{c:'🌼',n:'Daisy'},{c:'🌷',n:'Tulip'},{c:'🌱',n:'Seedling'},
    {c:'🪴',n:'Plant'},{c:'🌲',n:'Pine'},{c:'🌳',n:'Oak'},{c:'🌴',n:'Palm'},
    {c:'🌵',n:'Cactus'},{c:'🌾',n:'Grain'},{c:'🌿',n:'Fern'},{c:'☘️',n:'Clover'},
    {c:'🍀',n:'Shamrock'},{c:'🍁',n:'Maple'},{c:'🍂',n:'Leaf'},{c:'🍃',n:'Breeze'},
    {c:'🍄',n:'Mushroom'},{c:'🌰',n:'Chestnut'},{c:'🦀',n:'Crab'},{c:'🦞',n:'Lobster'},
    {c:'🦐',n:'Shrimp'},{c:'🦑',n:'Squid'},{c:'🌍',n:'Globe'},{c:'🌙',n:'Moon'},
    {c:'☀️',n:'Sun'},{c:'⭐',n:'Star'},{c:'⚡',n:'Bolt'},{c:'🌊',n:'Wave'},
    {c:'🔥',n:'Fire'},{c:'💧',n:'Drop'},{c:'❄️',n:'Snow'},{c:'🌬️',n:'Gust'},
    {c:'🎸',n:'Guitar'},{c:'🎺',n:'Trumpet'},{c:'🎻',n:'Violin'},{c:'🥁',n:'Drum'},
    {c:'🚀',n:'Rocket'},{c:'🚁',n:'Copter'},{c:'⛵',n:'Boat'},{c:'⚓',n:'Anchor'},
    {c:'⛺',n:'Tent'},{c:'🧭',n:'Compass'},{c:'🗺️',n:'Atlas'},{c:'🔮',n:'Crystal'},
    {c:'🪄',n:'Wand'},{c:'💎',n:'Gem'},{c:'🧲',n:'Magnet'},{c:'🔭',n:'Scope'},
    {c:'🔬',n:'Lens'},{c:'🛰️',n:'Satellite'},{c:'💡',n:'Bulb'},{c:'🔦',n:'Torch'},
    {c:'🏮',n:'Lantern'},{c:'📚',n:'Books'},{c:'📜',n:'Scroll'},{c:'🔑',n:'Key'},
    {c:'🎈',n:'Balloon'},{c:'🪁',n:'Kite'},{c:'🧸',n:'Teddy'},{c:'🧩',n:'Puzzle'},
    {c:'🚲',n:'Bike'},{c:'🛹',n:'Board'},{c:'🛼',n:'Skate'},{c:'🎫',n:'Ticket'},
    {c:'🏆',n:'Trophy'},{c:'🥇',n:'Medal'},{c:'👑',n:'Crown'},{c:'👻',n:'Ghost'},
    {c:'👽',n:'Alien'},{c:'👾',n:'Invader'},{c:'🤖',n:'Robot'},{c:'🦴',n:'Bone'},
    {c:'🦷',n:'Tooth'},{c:'👁️',n:'Eye'},{c:'🧠',n:'Brain'},{c:'❤️',n:'Heart'},
    {c:'🍎',n:'Apple'},{c:'🍐',n:'Pear'},{c:'🍊',n:'Orange'},{c:'🍋',n:'Lemon'},
    {c:'🍌',n:'Banana'},{c:'🍉',n:'Melon'},{c:'🍇',n:'Grape'},{c:'🍓',n:'Berry'},
    {c:'🫐',n:'Blueberry'},{c:'🍈',n:'Honeydew'},{c:'🍒',n:'Cherry'},{c:'🍑',n:'Peach'},
    {c:'🥭',n:'Mango'},{c:'🍍',n:'Pineapple'},{c:'🥥',n:'Coconut'},{c:'🥝',n:'Kiwi'},
    {c:'🍅',n:'Tomato'},{c:'🍆',n:'Eggplant'},{c:'🥑',n:'Avocado'},{c:'🥦',n:'Broccoli'},
    {c:'🥬',n:'Chard'},{c:'🥒',n:'Cucumber'},{c:'🫑',n:'Pepper'},{c:'🌶️',n:'Chili'},
    {c:'🌽',n:'Corn'},{c:'🥕',n:'Carrot'},{c:'🧄',n:'Garlic'},{c:'🧅',n:'Onion'},
    {c:'🥔',n:'Potato'},{c:'🍠',n:'Yam'},{c:'🥐',n:'Croissant'},{c:'🥯',n:'Bagel'},
    {c:'🍞',n:'Bread'},{c:'🥖',n:'Baguette'},{c:'🥨',n:'Pretzel'},{c:'🧀',n:'Cheese'},
    {c:'🥚',n:'Egg'},{c:'🍳',n:'Skillet'},{c:'🧈',n:'Butter'},{c:'🥞',n:'Pancake'},
    {c:'🧇',n:'Waffle'},{c:'🥓',n:'Bacon'},{c:'🥩',n:'Steak'},{c:'🍗',n:'Drumstick'},
    {c:'🍖',n:'Rib'},{c:'🌭',n:'Hotdog'},{c:'🍔',n:'Burger'},{c:'🍟',n:'Fries'},
    {c:'🍕',n:'Pizza'},{c:'🫓',n:'Flatbread'},{c:'🥪',n:'Sandwich'},{c:'🥙',n:'Pita'},
    {c:'🧆',n:'Falafel'},{c:'🌮',n:'Taco'},{c:'🌯',n:'Burrito'},{c:'🫔',n:'Tamale'},
    {c:'🥗',n:'Salad'},{c:'🥘',n:'Stew'},{c:'🫕',n:'Fondue'},{c:'🥫',n:'Can'}];
  function pick(a) { return a[Math.floor(Math.random() * a.length)]; }
  function hash(s) { var h = 0; for (var i = 0; i < s.length; i++) h = (h * 31 + s.charCodeAt(i)) | 0; return Math.abs(h); }
  function pickH(a, h) { return a[h % a.length]; }
  function makeAvatar(hex, emoji) {
    return 'data:image/svg+xml,' + encodeURIComponent(
      '<svg xmlns="http://www.w3.org/2000/svg" width="64" height="64">' +
      '<circle cx="32" cy="32" r="32" fill="' + hex + '"/>' +
      '<text x="32" y="32" text-anchor="middle" dominant-baseline="central" font-size="36">' +
      emoji + '</text></svg>');
  }
  function getSession() {
    var s = localStorage.getItem(KEY);
    if (s) {
      try {
        var p = JSON.parse(s);
        if (p && !p.avatarUrl) {
          var h = hash(p.guestId);
          var c = pickH(colors, h);
          var e = pickH(emojis, h >>> 5);
          p.avatarHex = c.hex;
          p.avatarChar = e.c;
          p.avatarUrl = makeAvatar(c.hex, e.c);
          p.displayName = pickH(adjectives, h >>> 10) + ' ' + c.name + ' ' + e.n;
          localStorage.setItem(KEY, JSON.stringify(p));
        }
        return p;
      } catch(_) {}
    }
    var guestId = 'guest-' + Math.random().toString(36).substring(2,10);
    var h = hash(guestId);
    var c = pickH(colors, h);
    var e = pickH(emojis, h >>> 5);
    var n = { guestId: guestId,
              displayName: pickH(adjectives, h >>> 10) + ' ' + c.name + ' ' + e.n,
              avatarHex: c.hex,
              avatarChar: e.c,
              avatarUrl: makeAvatar(c.hex, e.c),
              createdAt: Math.floor(Date.now()/1000) };
    localStorage.setItem(KEY, JSON.stringify(n));
    return n;
  }
  function avatarForAuthor(author) {
    var parts = (author || '').split(' ');
    if (parts.length >= 3) {
      var colorName = parts[1];
      var emojiName = parts.slice(2).join(' ');
      var c = colors.find(function(x) { return x.name === colorName; });
      var e = emojis.find(function(x) { return x.n === emojiName; });
      if (c && e) return makeAvatar(c.hex, e.c);
    }
    var h = hash(author || '');
    return makeAvatar(pickH(colors, h).hex, pickH(emojis, h >>> 5).c);
  }
  // Reset the local presentation session (name/avatar) to the anonymous values derived from its
  // own guest id, dropping any claimed-identity overlay. Used when the server reports no identity.
  function toAnon(current) {
    var h = hash(current.guestId);
    var c = pickH(colors, h);
    var e = pickH(emojis, h >>> 5);
    current.identity = null;
    current.displayName = pickH(adjectives, h >>> 10) + ' ' + c.name + ' ' + e.n;
    current.avatarHex = c.hex;
    current.avatarChar = e.c;
    current.avatarUrl = makeAvatar(c.hex, e.c);
    return current;
  }
  // The guest's generated anonymous pseudonym (Adjective Colour Emoji) derived from its own id — the
  // exact new-guest formula. Sent as the fallback name when disconnecting a provider drops back to
  // anonymous, so the STORED anonymous identity (switcher list + comment attribution) matches the
  // displayed one. avatarForAuthor(name) reparses the colour + emoji words, so the icon matches too.
  function anonName() {
    var h = hash(getSession().guestId);
    return pickH(adjectives, h >>> 10) + ' ' + pickH(colors, h).name + ' ' + pickH(emojis, h >>> 5).n;
  }

  // Reconcile the local DISPLAY session against the server's /api/auth/me body. Never touches typed
  // drafts (they live in the editor/model, not here). data.guest may be null: the server established
  // or confirmed a guest but exposes no identity (a fresh or anonymous guest), in which case any
  // stale claimed-identity display state is cleared.
  function applyServer(data) {
    var current = getSession();
    if (data && data.guest) {
      if (current.guestId !== data.guest.guestId) {
        var h = hash(data.guest.guestId);
        var c = pickH(colors, h);
        var e = pickH(emojis, h >>> 5);
        current = {
          guestId: data.guest.guestId,
          displayName: pickH(adjectives, h >>> 10) + ' ' + c.name + ' ' + e.n,
          avatarHex: c.hex,
          avatarChar: e.c,
          avatarUrl: makeAvatar(c.hex, e.c),
          createdAt: Math.floor(Date.now() / 1000)
        };
      }
      if (data.guest.identity && data.guest.identity.provider !== 'anonymous') {
        var id = data.guest.identity;
        current.identity = id;
        current.displayName = id.name;
        // Don't fall back to the cached avatarUrl — it may belong to a previously active identity.
        current.avatarUrl = id.picture || avatarForAuthor(id.name);
      } else {
        // No identity, OR the active identity is anonymous: always present the guest's own generated
        // pseudonym + matching icon (the same new-guest formula, derived from the guest id), never a
        // stale stored name (e.g. one an old disconnect inherited from a since-dropped provider).
        current = toAnon(current);
      }
      localStorage.setItem(KEY, JSON.stringify(current));
      return current;
    }
    // Guest established/confirmed with no identity: clear any obsolete claimed-identity state.
    if (current.identity) {
      current = toAnon(current);
      localStorage.setItem(KEY, JSON.stringify(current));
    }
    return current;
  }

  // One authority for the cookie and its presentation cache. A generation prevents a late
  // read from restoring an identity after logout/invalidation. Web Locks serialize cookie-
  // renewing reads and logout across tabs; the promise queue is the single-document fallback.
  var readyPromise = null;
  var generation = 0;
  var logoutPromise = null;
  var logoutPending = false;
  var queue = Promise.resolve();
  var LOGOUT_KEY = 'hedge_session_logout';
  function sessionLock(action) {
    if (typeof navigator !== 'undefined' && navigator.locks)
      return Promise.resolve().then(function() { return navigator.locks.request('hedge-session-cookie', action); });
    var next = queue.then(action, action);
    queue = next.catch(function() {});
    return next;
  }
  function stale() { return { ready: false, session: getSession() }; }
  function clearSession() {
    generation++;
    readyPromise = null;
    applyServer({guest: null});
    window.dispatchEvent(new CustomEvent('hedge:session-cleared'));
  }
  function announceLogout(phase) {
    localStorage.setItem(LOGOUT_KEY, JSON.stringify({phase: phase, nonce: Date.now() + ':' + Math.random()}));
  }
  window.addEventListener('storage', function(event) {
    if (event.key !== LOGOUT_KEY || !event.newValue) return;
    try {
      logoutPending = JSON.parse(event.newValue).phase === 'begin';
      clearSession();
    } catch (_) {}
  });
  // --- Mobile (Capacitor) bearer session -------------------------------------------------------
  // Feature-detected: when window.API_ORIGIN is set (a bundled mobile build) the session is an opaque
  // BEARER over the native transport, not a cookie. Everything below is untouched when it is unset, so
  // the web path is byte-for-byte the same.
  var MOBILE = !!(typeof window !== 'undefined' && window.API_ORIGIN);
  var API_BASE = MOBILE ? window.API_ORIGIN : (window.BASE_PATH || '');
  var BEARER_KEY = 'hedge_mobile_bearer';
  var PENDING_KEY = 'hedge_pending_revoke';
  // #7 — the bearer lives in a Keychain/Keystore-backed store (Capacitor SecureStoragePlugin) when the
  // plugin is present, with an in-memory cache so the native transport can read it SYNCHRONOUSLY per
  // request. Falls back to localStorage (dev / no plugin), migrating any legacy plaintext bearer into
  // the secure store once. The transport reads window.HedgeGuest.currentBearer() (= this cache).
  var bearerCache = '';
  var bearerWritten = false;   // #2 — a mutation (login/sign-out) after boot must win over the async load
  var secureStore = (function() {
    var p = (typeof window !== 'undefined' && window.Capacitor && window.Capacitor.Plugins && window.Capacitor.Plugins.SecureStoragePlugin) || null;
    if (p) return {
      get: function(k) { return p.get({ key: k }).then(function(r) { return (r && r.value) || ''; }).catch(function() { return ''; }); },
      set: function(k, v) { return (v ? p.set({ key: k, value: v }) : p.remove({ key: k })).catch(function() {}); }
    };
    return {
      get: function(k) { try { return Promise.resolve(localStorage.getItem(k) || ''); } catch (_) { return Promise.resolve(''); } },
      set: function(k, v) { try { v ? localStorage.setItem(k, v) : localStorage.removeItem(k); } catch (_) {} return Promise.resolve(); }
    };
  })();
  function getBearer() { return bearerCache; }
  function setBearer(t) { bearerWritten = true; bearerCache = t || ''; return secureStore.set(BEARER_KEY, bearerCache); }
  var bearerLoaded = secureStore.get(BEARER_KEY).then(function(v) {
    // #2 — a login/sign-out that ran while this read was in flight already set bearerCache; don't clobber it.
    if (bearerWritten) return bearerCache;
    if (v) { bearerCache = v; return v; }
    var legacy = ''; try { legacy = localStorage.getItem(BEARER_KEY) || ''; } catch (_) {}
    if (legacy && !bearerWritten) { bearerCache = legacy; secureStore.set(BEARER_KEY, legacy); try { localStorage.removeItem(BEARER_KEY); } catch (_) {} }
    return bearerCache;
  }).catch(function() { return ''; });

  // #1 — revoke a bearer SERVER-side; returns whether the server CONFIRMED it. A 500/network failure is
  // NOT swallowed as success — the caller reports it and persists the token for retry.
  function revokeBearer(token) {
    if (!token) return Promise.resolve(true);
    return fetch(API_BASE + '/api/mobile/signout', { method: 'POST', headers: { 'Authorization': 'Bearer ' + token }, cache: 'no-store' })
      .then(function(r) { return !!(r && r.ok); }).catch(function() { return false; });
  }
  // #1 — persisted pending-revoke set: tokens whose server revoke failed, retried opportunistically so a
  // valid token is never orphaned (we lost local possession but the server session lives).
  function loadPending() { return secureStore.get(PENDING_KEY).then(function(s) { try { return s ? JSON.parse(s) : []; } catch (_) { return []; } }); }
  // Serialize every read-modify-write of the pending set. Without this, a boot-time retry that reads the
  // list, then (after slow network revokes) writes back its result, would clobber a sign-out failure that
  // addPending queued in between — leaving that token valid until expiry. The lock makes each mutation
  // atomic; retryPendingRevokes additionally removes ONLY confirmed tokens so nothing is dropped blind.
  var pendingLock = Promise.resolve();
  function withPendingLock(fn) {
    var run = pendingLock.then(fn, fn);
    pendingLock = run.then(function() {}, function() {});   // keep the chain alive across rejections
    return run;
  }
  function addPending(token) {
    if (!token) return Promise.resolve();
    return withPendingLock(function() {
      return loadPending().then(function(list) {
        if (list.indexOf(token) < 0) { list.push(token); return secureStore.set(PENDING_KEY, JSON.stringify(list)); }
      });
    });
  }
  function retryPendingRevokes() {
    return withPendingLock(function() {
      return loadPending().then(function(list) {
        if (!list.length) return;
        return Promise.all(list.map(function(t) { return revokeBearer(t).then(function(ok) { return ok ? t : null; }); }))
          .then(function(results) {
            var confirmed = results.filter(function(t) { return !!t; });   // tokens the server CONFIRMED revoked
            // Re-read and subtract only confirmed tokens (never overwrite with a stale snapshot), so any
            // token queued in the meantime survives even if the lock above is ever loosened.
            return loadPending().then(function(latest) {
              var remaining = latest.filter(function(t) { return confirmed.indexOf(t) < 0; });
              return secureStore.set(PENDING_KEY, remaining.length ? JSON.stringify(remaining) : '');
            });
          });
      });
    }).catch(function() {});
  }
  bearerLoaded.then(retryPendingRevokes);   // retry any leftover revokes on boot
  // Mint an anonymous bearer on first launch so on-device commenting works before login.
  function ensureBearer() {
    return bearerLoaded.then(function() {
      if (getBearer()) return getBearer();
      return fetch(API_BASE + '/api/mobile/bootstrap', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: '{}' })
        .then(function(r) { return r.ok ? r.json() : null; })
        .then(function(d) { if (d && d.token) { return setBearer(d.token).then(function() { return d.token; }); } return ''; })
        .catch(function() { return ''; });
    });
  }
  // #5 — a dead bearer (revoked/expired) returns 401 from /me (a valid anon session is 200); clear it
  // and re-bootstrap a fresh anon ONCE so the session recovers instead of wedging on the stale token.
  function fetchMeMobile(epoch) {
    function meWith(token, allowRebootstrap) {
      if (epoch !== generation || !token) return Promise.resolve(stale());
      return fetch(API_BASE + '/api/mobile/me', { headers: { 'Authorization': 'Bearer ' + token }, cache: 'no-store' })
        .then(function(r) {
          // #2 — only recover from a 401 if THIS token is still the current bearer and the generation
          // hasn't moved; otherwise a stale /me (from before a login/sign-out) must not clear the new one.
          if (r.status === 401 && allowRebootstrap && epoch === generation && getBearer() === token)
            return setBearer('').then(ensureBearer).then(function(fresh) { return meWith(fresh, false); });
          if (!r.ok) return stale();
          return r.json().then(function(data) {
            if (epoch !== generation) return stale();
            return { ready: true, session: applyServer(data) };
          });
        }).catch(function() { return stale(); });
    }
    return ensureBearer().then(function(token) { return meWith(token, true); }).catch(function() { return stale(); });
  }
  function sha256hex(s) {
    return crypto.subtle.digest('SHA-256', new TextEncoder().encode(s)).then(function(b) {
      return Array.from(new Uint8Array(b)).map(function(x) { return x.toString(16).padStart(2, '0'); }).join('');
    });
  }
  function randomVerifier() {
    var a = new Uint8Array(32); crypto.getRandomValues(a);
    return Array.from(a).map(function(x) { return x.toString(16).padStart(2, '0'); }).join('');
  }
  // Browser-OAuth sign-in: open the SYSTEM browser (Google blocks OAuth in embedded WebViews), catch the
  // wtfail://auth?code deeplink, exchange the code (+ PKCE verifier + the current anon bearer) for a
  // verified bearer, then refresh. The anon guest's content is merged into the verified identity server
  // side (that's why the old bearer is presented). Rejects on cancel/failure.
  function signIn(provider) {
    provider = provider || 'google';
    var caps = (typeof window !== 'undefined' && window.Capacitor && window.Capacitor.Plugins) || {};
    if (!caps.Browser || !caps.App) return Promise.reject(new Error('Sign-in needs the Capacitor Browser/App plugins'));
    var startEpoch = generation;                 // #2 — a sign-out/invalidate during login makes this stale
    var verifier = randomVerifier();
    return sha256hex(verifier).then(function(challenge) {
      return new Promise(function(resolve, reject) {
        var settled = false;
        function finish() { settled = true; removeH(urlHandle); removeH(finHandle); }
        function removeH(h) { Promise.resolve(h).then(function(x) { if (x && x.remove) x.remove(); }).catch(function() {}); }
        var urlHandle = caps.App.addListener('appUrlOpen', function(event) {
          if (settled) return;
          var code = null; try { code = new URL(event.url).searchParams.get('code'); } catch (_) {}
          if (!code) return;                     // not our deeplink (or an error return) — let cancel handle it
          finish();
          try { caps.Browser.close(); } catch (_) {}
          fetch(API_BASE + '/api/mobile/exchange', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json', 'Authorization': 'Bearer ' + getBearer() },
            body: JSON.stringify({ code: code, verifier: verifier })
          }).then(function(r) { return r.ok ? r.json() : null; })
            .then(function(d) {
              if (!d || !d.token) { reject(new Error('Token exchange failed')); return; }
              if (startEpoch !== generation) {   // #2 — signed out mid-login: don't adopt it, revoke it
                revokeBearer(d.token).then(function(ok) { if (!ok) addPending(d.token); });
                reject(new Error('Sign-in was superseded')); return;
              }
              setBearer(d.token).then(function() { resolve(refreshSession()); });
            })
            .catch(reject);
        });
        // #6 — closing the browser without completing disposes the listener and rejects, so a later login
        // can't fire this stale verifier. The tick lets a deeplink arriving alongside the close win first.
        var finHandle = caps.Browser.addListener('browserFinished', function() {
          if (settled) return;
          setTimeout(function() { if (!settled) { finish(); reject(new Error('Sign-in was cancelled')); } }, 0);
        });
        caps.Browser.open({ url: API_BASE + '/api/auth/' + provider + '/login?returnTo=' + encodeURIComponent('/api/mobile/return?challenge=' + challenge) });
      });
    });
  }

  function fetchMe(epoch) {
    if (MOBILE) return fetchMeMobile(epoch);
    return sessionLock(function() {
      if (epoch !== generation || logoutPending) return stale();
      return fetch((window.BASE_PATH || '') + '/api/auth/me', { credentials: 'same-origin', cache: 'no-store' })
        .then(function(r) {
          if (!r.ok) return stale();
          return r.json().then(function(data) {
            if (epoch !== generation || logoutPending) return stale();
            return { ready: true, session: applyServer(data) };
          });
        }).catch(stale);
    }).catch(stale);
  }
  function refreshSession() {
    if (logoutPending) return Promise.resolve(stale());
    var epoch = generation;
    var current = fetchMe(epoch).then(function(res) {
      if (readyPromise === current && !res.ready) readyPromise = null;
      return res;
    });
    readyPromise = current;
    return current;
  }
  function ensureSession() { return readyPromise || refreshSession(); }
  // A protected operation must finish reading its response before releasing the lock. The
  // caller receives no stale private result after logout; queued writes are cancelled too.
  function withSessionRequest(action) {
    var epoch = generation;
    return ensureSession().then(function(ready) {
      if (!ready.ready) throw new Error('Your session is unavailable. Refresh and try again.');
      return sessionLock(function() {
        if (epoch !== generation || logoutPending) throw new Error('Your session changed. Refresh and try again.');
        return Promise.resolve().then(action).then(function(result) {
          if (epoch !== generation || logoutPending) throw new Error('Your session changed. Refresh and try again.');
          return result;
        });
      });
    });
  }
  function invalidateSession() { generation++; readyPromise = null; }
  // Existing display consumers keep their API. Authenticated-only consumers use the readiness
  // result: an unavailable server must not turn a cached display identity into authenticated UI.
  function syncSession() { return refreshSession().then(function(res) { return res.session; }); }
  function signOut() {
    if (MOBILE) {
      // Mobile sign-out: REVOKE the bearer server-side so a replayed token no longer authenticates, then
      // fall back to a fresh anonymous session. Returns the REVOKE result — false when the server didn't
      // confirm — and persists the token for retry (loses local possession, not the ability to revoke).
      // Does not touch other devices or the provider identity.
      clearSession();   // bump the generation synchronously so a login in flight is treated as stale
      // Coordinate with the initial secure-store load: before bearerLoaded resolves getBearer() is empty,
      // so a sign-out racing startup would setBearer('') (which also sets bearerWritten, suppressing the
      // pending load) and silently drop the stored credential WITHOUT revoking or queuing it. Await the
      // load first so `old` is the ACTUAL stored bearer.
      return bearerLoaded.then(function() {
        var old = getBearer();
        return setBearer('')
          .then(function() { return revokeBearer(old); })
          .then(function(ok) {
            var persist = ok ? Promise.resolve() : addPending(old);
            return persist.then(ensureBearer).then(function() { clearSession(); return ok; });
          })
          .catch(function() { return addPending(old).then(ensureBearer).then(function() { clearSession(); return false; }); });
      });
    }
    if (logoutPromise) return logoutPromise;
    logoutPending = true;
    clearSession();
    announceLogout('begin');
    logoutPromise = sessionLock(function() {
      return fetch((window.BASE_PATH || '') + '/api/auth/logout', {
        method: 'POST', credentials: 'same-origin', cache: 'no-store',
        headers: {'Content-Type': 'application/json'}, body: '{}'
      }).then(function(r) { return r.ok; }).catch(function() { return false; });
    }).catch(function() { return false; }).then(function(ok) {
      logoutPending = false;
      clearSession();
      announceLogout('end');
      logoutPromise = null;
      return ok;
    });
    return logoutPromise;
  }

  window.HedgeGuest = {
    getSession: getSession,
    avatarForAuthor: avatarForAuthor,
    anonName: anonName,
    syncSession: syncSession,
    refreshSession: refreshSession,
    ensureSession: ensureSession,
    invalidateSession: invalidateSession,
    withSessionRequest: withSessionRequest,
    signOut: signOut,
    // Mobile-only: browser-OAuth sign-in (no-op path on web, where the plugins are absent).
    signIn: signIn,
    isMobile: MOBILE,
    // The current bearer, read synchronously per request by the native transport (Client.Api).
    currentBearer: getBearer
  };
})();
