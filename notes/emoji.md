# Progressive Identification System for Hedge

The goal is to move from the current simple Adjective + Animal generation (576 combinations) to a more robust, Hedge-appropriate naming system using an Adjective + Color + Emoji/Noun format. This ensures a namespace large enough to prevent frequent collisions in a single-tenant environment of 100-1000 users.

## 1. Defining the Namespace

To reach ~600,000 combinations, we will use:
- **Emojis/Nouns**: 124 items
- **Colors**: 30 items
- **Adjectives**: 159 items

(124 × 30 × 159 = 591,480 total unique combinations. This means a 50% chance of collision happens at around ~920 active overlapping guests, which is more than enough entropy for Hedge communities before users rename them).

### Emojis/Nouns (124 items)
🐒🦍🐕🐩🐺🦊🐈🦁🐅🐆🐎🦄🦓🦌🐄🐂🐃🐖🐗🐏🐑🐐🐪🦙🦒🐘🦏🦛🐁🐀🐹🐇🐿️🦔🦇🐻🐨🐼🦥🦦🦨🦘🦡🦃🐔🐓🐦🐧🕊️🦅🦆🦢🦉🦩🦚🦜🐸🐊🐢🦎🐍🐉🦕🦖🐋🐬🦭🐟🐡🦈🐙🐌🦋🐛🐜🐝🪲🐞🦗🕷️🦂🦟🪰🪱🦠💐🌸💮🏵️🌹🥀🌺🌻🌼🌷🌱🪴🌲🌳🌴🌵🌾🌿☘️🍀🍁🍂🍃🍄🌰🦀🦞🦐🦑🌍🌙☀️⭐⚡🌊🔥💧❄️🌬️🎸🎺🎻🥁🚀🚁⛵⚓⛺🧭🗺️🔮🪄💎🧲🔭🔬🛰️💡🔦🏮📚📜🔑🎈🪁🧸🧩🚲🛹🛼🎫🏆🥇👑👻👽👾🤖🦴🦷👁️🧠❤️🍎🍐🍊🍋🍌🍉🍇🍓🫐🍈🍒🍑🥭🍍🥥🥝🍅🍆🥑🥦🥬🥒🫑🌶️🌽🥕🧄🧅🥔🍠🥐🥯🍞🥖🥨🧀🥚🍳🧈🥞🧇🥓🥩🍗🍖🌭🍔🍟🍕🫓🥪🥙🧆🌮🌯🫔🥗🥘🫕🥫

### Colors (30 items)
- Slate (`#64748b`)
- Gray (`#6b7280`)
- Zinc (`#71717a`)
- Neutral (`#737373`)
- Stone (`#78716c`)
- Red (`#ef4444`)
- Orange (`#f97316`)
- Amber (`#f59e0b`)
- Yellow (`#eab308`)
- Lime (`#84cc16`)
- Green (`#22c55e`)
- Emerald (`#10b981`)
- Teal (`#14b8a6`)
- Cyan (`#06b6d4`)
- Sky (`#0ea5e9`)
- Blue (`#3b82f6`)
- Indigo (`#6366f1`)
- Violet (`#8b5cf6`)
- Purple (`#a855f7`)
- Fuchsia (`#d946ef`)
- Pink (`#ec4899`)
- Rose (`#f43f5e`)
- Coral (`#ff7f50`)
- Salmon (`#fa8072`)
- Tomato (`#ff6347`)
- Gold (`#ffd700`)
- Olive (`#808000`)
- Navy (`#000080`)
- Maroon (`#800000`)
- Plum (`#dda0dd`)

### Adjectives (159 unique items)
Sleepy, Brave, Grumpy, Neon, Ancient, Quantum, Wandering, Clever, Daring, Gentle, Happy, Keen, Lively, Merry, Noble, Proud, Quick, Sharp, Swift, Tall, Warm, Wild, Wise, Bold, Bright, Cool, Fair, Calm, Fierce, Eager, Sunny, Lucky, Jolly, Silly, Chilly, Cosmic, Mystic, Lunar, Solar, Stellar, Astral, Galactic, Epic, Heroic, Magic, Secret, Hidden, Lost, Found, Quiet, Loud, Fuzzy, Spiky, Smooth, Rough, Soft, Hard, Sweet, Sour, Spicy, Salty, Bitter, Fresh, Stale, Crisp, Crunchy, Chewy, Sticky, Slippery, Shiny, Dull, Dark, Light, Heavy, Empty, Full, Hollow, Solid, Liquid, Gas, Hot, Cold, Freezing, Boiling, Fast, Slow, Sluggish, Rapid, Leisurely, Hasty, Deliberate, Young, Old, New, Modern, Classic, Vintage, Retro, Tiny, Small, Medium, Large, Huge, Giant, Massive, Colossal, Good, Bad, Great, Terrible, Excellent, Awful, Wonderful, Horrible, Sad, Joyful, Sorrowful, Glad, Upset, Cheerful, Miserable, Angry, Furious, Peaceful, Mad, Tranquil, Enraged, Serene, Cowardly, Courageous, Fearful, Fearless, Timid, Afraid, Smart, Stupid, Foolish, Intelligent, Ignorant, Unwise, Rich, Poor, Wealthy, Impoverished, Affluent, Destitute, Prosperous, Needy, Beautiful, Ugly, Gorgeous, Hideous, Attractive, Unattractive, Handsome, Plain, Clean, Dirty, Spotless, Filthy, Immaculate, Grubby, Pristine, Messy, Dry, Wet, Arid, Damp, Parched, Moist, Dehydrated, Soaked.

## Proposed Changes

We will apply these dictionaries to both the client-side module and the F# scaffold that builds it.

### `apps/microblog/lib/guest-session.js`
- Overhaul `adj` and `ani` arrays with the new `adjectives`, `colors`, and `emojis` arrays.
- Update `getSession()` to pick one of each and format the `displayName` as `Adjective Color EmojiName` (e.g., "Sleepy Teal Ghost").
- Include `avatarHex` and `avatarChar` in the returned session object.

### `packages/hedge/src/Gen/Scaffold.fs`
- Update the F# multi-line string that writes `guest-session.js` to emit the new dictionaries and logic.
