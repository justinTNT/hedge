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

  // Single-flight bootstrap. `readyPromise` holds the last SUCCESSFUL /api/auth/me (the signed
  // cookie is set, the session is ready); a failure is not cached, so a later call retries.
  var readyPromise = null;

  // Fetch /api/auth/me and resolve an explicit readiness result. `ready` is true ONLY on an HTTP
  // success (the server bootstrapped/renewed the session and set the signed cookie); a network
  // error or non-2xx yields ready:false with the cached session for DISPLAY only. Never rejects,
  // so callers get a definite answer to gate writes on.
  function fetchMe() {
    var basePath = window.BASE_PATH || '';
    return fetch(basePath + '/api/auth/me', { credentials: 'same-origin' })
      .then(function(r) {
        if (!r.ok) return { ready: false, session: getSession() };
        return r.json().then(function(data) { return { ready: true, session: applyServer(data) }; });
      })
      .catch(function() { return { ready: false, session: getSession() }; });
  }

  // Always fetch fresh (a display sync or a re-sync after an identity op needs current server
  // state), and refresh the single-flight readiness cache from the result.
  function refresh() {
    readyPromise = fetchMe().then(function(res) { if (!res.ready) readyPromise = null; return res; });
    return readyPromise;
  }

  // Readiness gate for writes: reuse the in-flight/succeeded bootstrap if there is one, else start
  // one. The FIRST comment/upload (including a deep link) awaits this, so the signed cookie exists
  // before the write. Resolves { ready, session }; ready:false means the write should not proceed.
  function ensureSession() { return readyPromise || refresh(); }

  // Drop the cached bootstrap so the next ensureSession re-fetches /api/auth/me. Call this when a
  // write is rejected with 401 (the cookie expired, was cleared, or the signing key changed since
  // bootstrap): the cached readyPromise would otherwise keep resending the same rejected credential
  // until a full page reload. After invalidation the next attempt re-bootstraps (a fresh signed
  // guest if the old cookie is gone) and can succeed.
  function invalidateSession() { readyPromise = null; }

  // Display sync (identity component boot + post-merge/disconnect re-sync): always fresh, returns
  // the session object, and (re)establishes readiness for write gating.
  function syncSession() { return refresh().then(function(res) { return res.session; }); }

  window.HedgeGuest = {
    getSession: getSession,
    avatarForAuthor: avatarForAuthor,
    anonName: anonName,
    syncSession: syncSession,
    ensureSession: ensureSession,
    invalidateSession: invalidateSession
  };
})();
