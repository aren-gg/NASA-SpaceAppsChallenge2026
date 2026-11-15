# Mars expedition interface

The supplied image informs the chunky frames, colored resource medallions and grouped action slots. All shipped graphics are original procedural polygons, with matte copper plates and warm Mars colors; no stock art or image textures were copied.

## Comparable games
- [Astroneer: Mission, Compass and Power update](https://blog.astroneer.space/p/the-mission-compass-and-power-update-patch-notes/) documents mission access from the backpack, objective-change notifications and compass direction/distance/elevation. Its official compass screenshot was inspected. Application here: keep navigation visible, put secondary controls in the expedition pack and leave the center of the terrain clear.
- [Subnautica: official Xbox manual](https://dlassets-ssl.xboxlive.com/public/content/ec3cb120-5fdc-4472-aa43-7aee97561bf8/GameManual/3fa38265-adff-43b4-a56b-62f6a5506d6c/en-US/index.html) describes oxygen/supply-limited exploration, replenishment at shelter and the PDA. Application here: oxygen is first, supplies are direct actions, and management belongs in an opened panel. This is a design inference, not a claim about its exact HUD arrangement.
- [Planet Crafter: developer press kit](https://mijugames.com/pages/planetcrafter/presskit.html) describes open-world space survival with oxygen, thirst, temperature and health. Application here: show distinct named resources, not an undifferentiated status paragraph.

## Decisions
Oxygen, health, water, food and rest form a compact left cluster. Each has a symbol, label, numeric percentage and meter so meaning does not depend on color. Eat [F] and Drink [G] stay visible during exploration; Full explicitly explains their disabled state. Paused missions disable actions. Pack [E] opens management on the right while keeping vitals visible; Research [R] remains available from the pack or keyboard. Escape opens the menu and closes modal panels.

UI/UX Pro Max was found in the local skill collection and its claymorphism guidance used for tactile forms and clear hierarchy, adapted to flat low-poly geometry rather than glossy shadows or web components. Existing readable typography and large-text settings remain.

Caveman CLI was found and its shrink wrapper was attempted. The compression engine is not installed: the receipt reported passthrough, 1,863 bytes in/out and zero savings. No global hooks, proxy routing or configuration were installed. Searches and tool output were bounded manually.

## Requested font update
The Counter Striken reference matches [Counter-Strike Regular by SoJa](https://www.dafont.com/counter-strike.font). The original TTF is bundled under Assets/Fonts with its source attribution. A static TextMeshPro SDF atlas supplies the UI font throughout the HUD, pack and menus. Liberation Sans supplies unsupported punctuation/symbols and hyphens, because the display font maps its hyphen to a soldier silhouette.

## Readability refinement
Title capitalization, 30-pixel reference body text, modest extra word/line spacing and a 94%-width font derivative now support the shared indented key/action control rows. See [readability validation](readability-validation.md) for current captures and checks.
