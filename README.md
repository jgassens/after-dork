# After Dork — chemistry screen savers

Retro screen savers for macOS in the spirit of After Dark and the classic
Windows savers, parodied for the chemistry lab. Everything is drawn
procedurally in Swift (Core Graphics + a software raycaster) — no assets,
no Xcode project.

| Saver | Parody of | Gag |
|---|---|---|
| **Flying Flasks** | After Dark "Flying Toasters" | Winged Erlenmeyers flap across a starfield; NMR tubes drift by as the toast; rare round-bottom flask on bat wings |
| **Glassware Pipes** | Windows "3D Pipes" | Glass tubing grows joint by joint with ball joints, frosted ground-glass collars, and condenser coils, until the hood is full and it flushes |
| **Lattice Maze** | Windows 95 "3D Maze" | First-person crawl through a MOF: metal nodes, strut walls, checkerboard floor; bumping into a spinning solvent molecule (benzene, water, C60) flips you upside down |
| **Stoddart Reef** | After Dark "Fish!" | An aquarium of mechanically interlocked molecules: bistable rotaxanes with shuttling blue boxes, a [2]catenane, Borromean rings, a crown-ether jellyfish (K+ aboard), a ferrocene crab, PEG seaweed, and a MOF crystal castle |
| **Mystify Polymers** | Windows "Mystify" | Two DNA double helices (color-coded base-pair rungs, 5·3 ends) and a lettered RNA strand wiggling through conformations, plus a benzene ring bouncing DVD-logo style |

## Build & install

```sh
make            # build all four .saver bundles into build/
make install    # copy them to ~/Library/Screen Savers
make previews   # render PNG frames offscreen into build/shots/ (no install needed)
make clean
```

After `make install`, pick them in **System Settings → Screen Saver** (they
appear under "Other"). Universal binaries (arm64 + x86_64), ad-hoc signed,
macOS 11+.

## Layout

Each saver is one self-contained `ScreenSaverView` subclass in
`<Name>/<Name>.swift` plus an `Info.plist`. `Harness/main.swift` compiles
together with any saver (`-DHARNESS`) into an offscreen preview binary that
steps the animation and writes PNG snapshots — that is how the visuals were
tuned.
