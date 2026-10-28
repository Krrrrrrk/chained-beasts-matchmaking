# Third-party artwork

The menu and ping icons use Kenney artwork dedicated to the public domain under
[CC0 1.0 Universal](https://creativecommons.org/publicdomain/zero/1.0/).

| Icons | Original pack | Author |
| --- | --- | --- |
| Target, party, lock | [Game Icons](https://kenney.nl/assets/game-icons) | Kenney |
| Heart, sad face | [Emotes Pack](https://kenney.nl/assets/emotes-pack) | Kenney |

The original pack pages identify their license as CC0. The five original PNGs
are kept in `assets/icons/sources/`, with exact download URLs and SHA-256 hashes
in `assets/icons/manifest.json`. The downloads came from the public
[shorepine/kenney mirror](https://github.com/shorepine/kenney) at commit
`3694c6879e487c108f55677be7dd2ca75b07cc3b`; the mirror's root license describes
a different pack, so the pack-specific original license pages are the authority.

Adaptations: crop transparent padding, center within a padded 64×64 alpha mask,
store rows bottom-up for Unity, and tint with the menu palette at runtime.
The plugin embeds those masks; it does not fetch artwork at runtime. No game
artwork or other icons from these packs are redistributed.

Kenney does not require attribution under CC0; this notice records provenance.
The repository's code license does not replace the artwork's CC0 dedication.
