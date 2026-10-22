# AMS2 (Madness) track formats: BFF container and track contents

This is a static reverse-engineering spec. It covers the Sochi community mod (made with "Open Madness Track Tools 0.1.0", OMTT) and the official paks, read only.

Legend:
- **[V]** verified, by byte comparison or by parsing every file.
- **[H]** hypothesis, not yet confirmed in game.

All integers are little-endian unless noted.

## 1. BFF container

### Header (0x130 bytes) [V]

| off | type | value |
|---|---|---|
| 0x00 | char[4] | `' KAP'` |
| 0x04 | u32 | version `0x10400004` |
| 0x08 | u32 | entry count n |
| 0x0C | u32 | data_offset (first entry) |
| 0x10 | u32 | 0 |
| 0x14 | u32 | alignment `0x10` |
| 0x18 | char[0x100] | pak name (`sochi_ams`, `sochi_ams_Physics`, `SUM_sochi_ams` ...), NUL padded |
| 0x118 | u32 | toc_size = 42*n |
| 0x11C | u32 | 0 |
| 0x120 | u32 | ext_size |
| 0x124 | u32 | dhsa_offset |
| 0x128 | u32 | dhsa_size |
| 0x12C | u32 | flags. Byte 0x12D = key index: 0 in community paks, 2 in official paks |

### TOC (starts at 0x130) [V]

The TOC has n records of 42 bytes each, packed as `<QQIIQHI4s`:
- uid u64
- offset u64 (absolute)
- pak_size u32 (stored bytes)
- size u32 (decoded bytes)
- timestamp u64
- comp u16
- crc u32 = `~crc32(stored bytes)`
- ext char[4] (file extension, NUL padded)

**Ordering [V]:** entries are sorted by ordinal byte order of the normalised name. The sort is case-sensitive and the separator is `\`. With this ordering, the repack is byte-identical.

**comp [V]:**
- `0x0001`: zlib stream.
- `0x0000`: stored.
- Official paks use `0x0401` (RC4, then zlib) and `0x0400` (RC4 only). The RC4 key is reset for every entry, and the CRC is computed over the encrypted bytes.

**Official TOC [V]:** it is RC4-encrypted with key index 2, key `b'vNva6iM!Cj3DWbVf0OKA0v'`.

**timestamp [H]:** the bit layout is `year<<48 | mon<<44 | day<<39 | hour<<34 | min<<28 | sec<<22 | ms<<12`. The engine does not appear to check it.

### uid [V]

The uid is Bob Jenkins **lookup8** (64-bit) over the normalised name:
- Normalisation: ASCII lowercase, and `/` becomes `\`.
- level = 0.
- 24-byte blocks are read as big-endian words built from *signed* bytes.
- The tail is the standard lookup8 tail; the function returns `c`.
- An empty name gives `0x8db63936938575bf`.
- In AMS2AVX.exe: getter at 0x140c73f60, hash at 0x140c5ebe0. The function at 0x140c5e920 is lookup2 (32-bit) and is used for other ids.
- Checked on 2737/2737 Sochi names and on resolved names from official paks.
- Code: `uidhash.py`.

### EXT block (names) [V for community paks]

Layout:
1. Magic `' TXE'`.
2. u32 ext_data = 16n + string bytes.
3. author char[0x200] (`"Reiza.xml"`).
4. platform char[0x100] (`"PC"`).
5. n records of `(u64 absolute offset of string, u64 timestamp>>12)`.
6. Strings: u8 length followed by the bytes, with no NUL.
7. Zero padding so that records plus strings are a multiple of 16.

In official paks the EXT block is encrypted with an unknown algorithm (it is not the same RC4). Names can still be recovered by hashing candidate names; see `guess.py`.

### DHSA block [V]

Layout: `'DHSA'`, u32 `0x10000000`, u32 count, u32 0, then 16-byte records `(first entry index, entry count, byte span from first.offset to end of last entry, 0)`.
- Sochi uses one record per entry, with span = pak_size.
- Official paks group consecutive entries, and some entries are not covered at all.

**Data:** data_offset = align16(end of DHSA), and each entry starts on a 16-byte boundary.

**Compression:** zlib level 6 reproduces many OMTT entries but not all of them. The engine only needs a valid stream.

### Validation [V]

- `validate.py`: 4299/4299 CRCs are OK, and every decoded size matches. Paks checked: all Sochi paks, Interlagos_Physics, SUM_Interlagos_1991, Interlagos[Group], Bathurst_2020_Physics, InterlagosKart1, WIN_fontana_oval_historic.
- `bff_write.py repack`: sochi_ams.bff (109 MB), sochi_ams_Physics.bff and SUM_sochi_ams.bff are **byte-identical** to the originals.
- `recompress`: round-trips to the same content.

## 2. Mod layout (community track) [V, Sochi]

```
Automobilista 2\Pakfiles\Tracks\<t>.bff                 meshes .meb, materials .bmt, textures, sgx/enx/trd, trackproperties.bin, cameras\*.xml
Automobilista 2\Pakfiles\Tracks\<t>_Physics.bff         tracks\_data\aiw\<t>.aiw, tracks\<t>\physics\{object_properties.xml,<t>.csm,triggers.xml}, tracks\<t>\track_cut\<t>.gcl
Automobilista 2\Pakfiles\Tracks\{SUM,WIN,AUT,SPR,SNO}_<t>.bff   2 entries: tracks\_data\omtt\seasonal.{bmt,meb}
Automobilista 2\Tracks\<t>\<t>.trd + every material as .mtx and .bmt (loose)
Automobilista 2\GUI\{tracklogos 512x512, trackmaps 1000x1000, trackmaps3d 1000x1000, trackphotos 1920x1080}\<t>.dds  (DXT5)
```

Official physics paks also contain `.mrd`, `.bml` and two other xml files. Their purpose is unknown.

## 3. MEB mesh [V: all 2227 Sochi files parse with exact length]

```
u32 0x01000000, u32 0x100, NAME (UPPERCASE, NUL, pad to 4)
u32 nverts, u32 nstreams, u32 nsub
f32[10] bounds = sphere centre xyz, radius, AABB min xyz, AABB max xyz
nstreams x { u32 fmt (D3DDECLTYPE: 1 float2, 2 float3, 4 D3DCOLOR), u32 semantic (0 pos, 6 colour, 2 normal, 4 tangent, 5 binormal, 3 uv), u32 index, data }
   Sochi stream order: pos, colour, normal, tangent, binormal, uv
nsub x { MATERIAL PATH "TRACKS\<T>\<MAT>.MTX" (upper, NUL, pad 4), u32 0, u32 ntris,
         indices u16 (u32 if nverts > 0xffff [H]), pad 4, u16 vmin, u16 vmax, f32[10] bounds }
```

**Bounds:** sphere centre = (min+max)/2 and radius = |max−centre|, computed in float32.
- With the original bounds, the rewrite is identical in 2205/2207 files.
- With recomputed bounds, 1370/2227 files are identical; the rest differ in the last float bit.

**Winding [V]:** `cross(b-a, c-a)·normal > 0` for 99.97% of triangles. Most materials use `cull="EBFCT_ANTICLOCKWISE"`.

## 4. BMT = binary XML ('BLMY') compiled from .mtx [V: 289/297 Sochi files byte-identical]

**Header:** `'BLMY'`, u32 7, u32 total size, u32 0. It is followed by 7 descriptors `(tag, size, offset, 0)` for the chunks below.

| chunk | content |
|---|---|
| HEAD | 7 u32: nElems, nAttrs, nColls, nNums, string bytes (or string count in the "packed" variant), nBools, 0 |
| ELMT | 28-byte records: hash, first attribute of the subtree, nattr, nchild, first child, next sibling, −1. The root comes last, children in pre-order. Attributes are stored post-order: child attributes first, then the element's own |
| ATTR | 20-byte records: hash, type (0 = number, index into NUMB f32; 1 = bool, bit index into BOOL; 2 = string, offset into STRS), 1, −1 |
| COLL | 16-byte records: (hash, first index, count, −1). Element names sorted by hash, then attribute names sorted by hash |
| STRS | deduplicated, NUL-terminated strings, each padded to 4 (OMTT) |

- **Name hash:** `h = rotl(h,5)*31 + c` (32-bit).
- **Last chunk:** STRS is not padded at the end.
- **Variants:** 7 files use the "packed strings" variant, which `bmt.py` also supports. The other 8 files come from a different writer that orders attributes differently.

**Material .mtx (XML):**
- Main shader: `Render\Shaders\rz_basic.fx`, technique `rz_basic`.
- Parameters: detailTilingX/Y, diffuseMultiplier, emissiveScale, globalSpecularFactor, max/minSpecPower, reflectionBlurBias, diffuse1Texture (`tracks\textures\<t>\X.DDS`).
- Also present: depthparams, alphablendparams, and `define USE_ALPHATEST` etc.
- Translucent materials use `basic_translucent.fx` (technique Basic_Translucent, diffuseTexture/specularTexture).
- Textures are DDS (DXT1, DXT3 or DXT5).
- **[H]** The game probably reads only the .bmt and ignores the .mtx; OMTT ships both.

## 5. Scene and track description [V structure, copied from Sochi]

- **`<t>.sgx`** (XML):
  - Root: `SCENE FileVersion="0.1.0.0" NumObjects NumPartitions="1"`.
  - One `OBJ_ID` per mesh, containing `NODE type="OBJECT" Name MatrixNumber="-1" instances="1" userflags="1048693"`. Inside it: `RESOURCE Filename="tracks/<t>/<x>.meb"`, `SPHERE Centre="x y z 1" Radius`, and `MATRIX Offset="0 0 0" Orientation="0 0 -0 -1" Scale="1"`.
  - One `PARTITION_ID` with `AABBOX`, `CHILD_PARTITIONS IDs="NONE"` and `CHILD_OBJS IDs="1 2 ..."`.
- **`<t>.enx`:** `ENVIRONMENT FileVersion="0.1.0.0"` with an empty `SKYDOME`.
- **`<t>.trd`:** reflection XML (TrackDetails) with Name, TrackName, ScenegraphFile, Length, Environment File (`Interlagos.wdf`), TimeOfDay Group, PitSpeedLimit, Max AI participants, Track_Latitude, and so on. It is stored both in the pak and loose.
- **`trackproperties.bin`, `cameras\cameraconfig.xml`:** not decoded. The converter copies Sochi's versions **[H]**.
- **`cameras\<t>.xml`** (TV cameras) [V structure, Sochi]:
  - Reflection XML: `CCameraDataSet` with three Fct props: `Trackside Cams`, `Splines` (0 on Sochi) and `Areas`.
  - `Trackside Cams` = `CTrackingCamData` (45 on Sochi; ids 0xA5C10000+i). Each has Pos, QuatOri `1;0;0;0` (tracking cams aim themselves), FOV/zoom/DOF fields and `ActiveAreas elements=N` + `<funcpropdata areaIndex1=".." …/>` = indices into `Areas`.
  - `Areas` = `CSphereArea` (1449 on Sochi; ids 0xA5B20000+i): Name `<cam>_visible_NNN`, Centre = AI waypoint + (0, 0.8, 0), Radius 6.5. Each camera owns a contiguous run of spheres along the line; the camera is live while the car is inside one of its spheres.
  - The class declarations at the top also contain `<prop name="Trackside Cams" type="Fct" />`; the data starts at `<prop name="Trackside Cams" elements=`.

## 6. AI line `.aiw` (rFactor-style text) [V structure]

Sections:
- `[Features]` (waypointspan, pitlanes, startinggrid, pitspots, garagespots …).
- `[GRID]` (GridIndex/Pos/Ori).
- `[ROLLING START]`.
- `[TELEPORT]` (same content as GRID).
- `[CLIPPING POINTS]`.
- `[PITS]` (TeamIndex, PitLeftHanded, PitPos/PitOri, GarLeftHanded/GarPos/GarOri ×2).
- `[Waypoint]`: header (trackstate, times, number_waypoints, lap_length, sector_1/2_length …), then per waypoint `\\N` with wp_pos, wp_perp, wp_width, wp_dwidth, wp_path, wp_galpha, wp_score, wp_groove_lat, wp_event, wp_branchID, wp_bitfields, wpd_CornerType, wpd_CornerState and WP_PTRS.

Derived from Sochi:
- **Main loop:** waypoints 0..1100 (branchID 0) form the main loop (the sum of steps equals lap_length 5792 m).
- **WP_PTRS = (prev, next, link, branchID)** [V on Sochi]. Index order is the driving direction [V]: the sector-1 trigger falls at index-distance 2341 = sector_1_length.
- **wp_score = (sector index, lap distance)**; sector = 0 below sector_1_length, 1 below sector_2_length, else 2 [V: 0 mismatches on 1101 wps].
- **wp_pos / wp_width / wp_path** [V on Sochi]: wp_pos is the track centre, wp_width=(left,right) the edge distances (left = −perp side), wp_path the lateral offset of the racing line (line = pos + path·perp). Negative path is bounded by the first width (0 violations in 819 wps); Sochi path ranges −7.4..+5 m.
- **wp_perp** = (dz, 0, −dx) of the driving direction [V on Sochi main 1101/1101 and pit 301/303].
- **Ori yaw** = atan2(−fx, −fz), so the car faces (−sin y, 0, −cos y) [V]. The converted grid agrees with the AI line within 1°.
- **Pole position:** pole is the grid slot furthest *ahead* of the S/F trigger (≈220 m on Sochi) [V].
- **Pit-lane branch** [V structure on Sochi, `aiw_pit.py`]:
  - `pitlanes=1`. Pit waypoints follow the main loop contiguously (Sochi 1101..1403, branchID 1, bitfields 0, width 6/6, dwidth 10/10, path 0).
  - Fork: the main wp where the pit leaves (Sochi 1003) has `link` = first pit wp; the first pit wp has prev = fork. Main wps keep prev/next on the loop (no main wp has next into the pit).
  - Join: the last pit wp has next = link = the main wp where it rejoins (Sochi 202).
  - Other pit wps: link = nearest parallel main wp where the lanes run side by side (31 of 303), else −1.
  - Pit score = (sector of the fork, fork distance + distance along the pit), monotone and > lap_length after S/F.
  - Stall spurs: one wp per PitPos (wp_pos == PitPos exactly), bitfields 1, own branchID (Sochi 106..129, 169), WP_PTRS = (pit wp 2 before, itself, pit wp 2 after, branchID), path = (−lateral offset from the pit line, 0), width 5/5, galpha 0.288.
  - Sochi also has 24 spurs hanging off main wps 5..39 (branchIDs 2..25), probably grid/garage related [H]; not generated.

## 7. Physics

- **`triggers.xml`** [V structure]: `TriggerObjectManager` → `ShapeDesc` boxes with Width 2, Height 120, Length = across the track, Material CRC, Relative Position (on the road) and Relative Orientation (rows = [travel dir; (0,−1,0); perp]).
  - Roles by matching the trigger positions to the Sochi AI line:
    - `4110219313` = start/finish (at wp 0) [H, strong].
    - `2964580884` = end of sector 1 (at 2341 m = sector_1_length) [H, strong].
    - `2964580887` = end of sector 2 (at 4517 m = sector_2_length) [H, strong].
    - Six 25 m boxes (CRCs 2734965195/…192, 1583431227, 3439599616, 747266536, 3456376832) [H]: probably DRS detection/activation.
    - 13.6 m and 9.0 m boxes [H]: possibly pit-related.
  - The CRC names are **not** resolved: neither name_hash nor crc32 of 108k exe strings matched.
- **`<t>.csm`** = PhysX **3.2** cooked triangle meshes (not 2.8: official physics XMLs say `PhysX30Collection Version=3.2.0-SMS`) [V: all 1493 Sochi meshes parse to exact length and every R-tree reaches each triangle exactly once; `csm.py`].
  - File: u32 330, then N × `[u32 size][mesh][u32 surface id]`.
  - Mesh (LE): `NXS\x01` `MESH`, u32 version 12, u32 flags (1 materials, 2 face remap, 4 8-bit idx, 8 16-bit idx, 16 adjacency; Sochi always 0x0a), f32 convexEdgeThreshold 0.001, u32 nv, u32 nt, nv×3 f32, indices (u8/u16/u32 by flags), remap = u32 maxId + nt ids (u8/u16/u32 by maxId).
  - RTree (**big-endian**): `RTRE`, u32 1, bmin(4f, w 0), bmax(4f, w 0), invDiag (1,1,1,1), dScaler = (bmax−bmin)/65535 (w 0), u32 pageSize 4, nRootPages 1, nLevels, totalNodes = 4×pages, totalPages, unused 0. Pages of 112 bytes: minx[4] miny[4] minz[4] maxx[4] maxy[4] maxz[4] (f32), ptr[4] (u32). Inner ptr = page index × 112; leaf ptr = (firstTri<<5) | ((count−1)<<1) | 1, count 1..16, leaves cover contiguous triangle runs. Empty slot: min = FLT_MAX, max = −FLT_MAX, ptr 29. Boxes inflated ≈5e-4; bmin/bmax = root box.
  - Trailer (LE): f32 geomEpsilon = f32(max|coord|)·2^−22 [V 1493/1493], aabb 6 f32 = vertex min/max [V], u32 nt, nt bytes extraTrigData.
  - extraTrigData: bits 8/16/32 = edge 01/12/20 active. Active = boundary, non-manifold, or convex with cos(n0,n1) < cos(0.1) [V: 99.90% of edges, 340/400 meshes exact].
  - **Surface id = the shared-memory terrain enum** (Type#11, `TERRAIN_*`, in the game's `Support/SharedMemory/.../SharedMemory.h`) [V, 22/10]: 0 ROAD, 1 LOW_GRIP_ROAD, 2-4 BUMPY_ROAD1-3, 5 MARBLES, 6 GRASSY_BERMS, 7 GRASS, 8 GRAVEL, 9 BUMPY_GRAVEL, 10 RUMBLE_STRIPS, 11 DRAINS, 12 TYREWALLS, 13 CEMENTWALLS, 14 GUARDRAILS, 15 SAND, 16 BUMPY_SAND, 17 DIRT, 18 BUMPY_DIRT, 19 DIRT_ROAD, 20 BUMPY_DIRT_ROAD, 21 PAVEMENT, 22 DIRT_BANK, 23 WOOD, 24 DRY_VERGE, 25 EXIT_RUMBLE_STRIPS, 26 GRASSCRETE, 27 LONG_GRASS, 28 SLOPE_GRASS, 29 COBBLES, 30 SAND_ROAD, 31 BAKED_CLAY, 32 ASTROTURF, 33 SNOWHALF, 34 SNOWFULL, 35 DAMAGED_ROAD1, 36 TRAIN_TRACK_ROAD, 37 BUMPYCOBBLES, 38 ARIES_ONLY, 39 ORION_ONLY, 40 B1RUMBLES, 41 B2RUMBLES, 42 ROUGH_SAND_MEDIUM, 43 ROUGH_SAND_HEAVY, 44 SNOWWALLS, 45 ICE_ROAD, 46 RUNOFF_ROAD, 47 ILLEGAL_STRIP, 48 PAINT_CONCRETE, 49 PAINT_CONCRETE_ILLEGAL, 50 RALLY_TARMAC.
  - Evidence: (a) Sochi render materials (`csm_sid.py`): 0 asphalt/pit, 7 grass, 10 kerb, 12 tyre wall, 13 concrete wall, 14 armco = 6/6 enum names. (b) Geometry of the official csm (first 49 tracks below; the full run over 240 layouts agrees: 8 on 135, 46 on 186, 32 on 40 layouts at the edge/1.5-5 m right after kerbs 10/40, 15 on 17 layouts far out after grass; area per id banded by distance to the AIW edges, scratch `geo.py`/`summ.py`): 0 under the AI line on 46 tracks; 7 = 71% "far", first surface after 0; 8 on 33 tracks, all horizontal, 0% on track, 5-40 m out, mostly after 7 (gravel traps); 46 on 41 tracks, off track, directly after 0 (paved runoff; 56% of barcelona_rx centre); 19 = 100% of the centre of ascurra_dirt/ascurra_rx; 10/40/41 at the edge after 0; 12/13/14 vertical; 15 only on 4 tracks, 71% "far" (beach/decor sand, not traps); 32 only 3 tracks, 1.3k m², 5-40 m after 7.
  - Confidence for the converter: **high** 0, 7, 8, 10, 12, 13, 14, 32, 46 (also 19, 40, 41, not used). Grip of each id in game not measured (PHYSICSPERSISTENT not decrypted). Do not use the others (1-6, 9, 11, 15-31 except 19, 33-39, 42-45, 47-50) without an in-game check.
  - **Writer `csm_write.py`** (own cooker, not PhysX): drops degenerate tris (remap keeps original ids), median-split leaves of ≤4 tris, pages of 4 built bottom-up, pages numbered top-down (root 0), inflation 5e-4, header/trailer rules above. [V] Re-cooking all 1493 Sochi meshes: 0 tree errors, 1160 vertical ray casts tree vs brute force, 0 mismatches. [H] that the game's PhysX 3.2 loads it (layout equal; the tree is valid but not byte-identical to PhysX's).
- **`<t>.gcl`** (track cut / track limits) [V: `gcl.py` re-encodes Sochi byte-identically]:
  - LE: u32 0x10000001, u32 ntri, f32 cell 100, f32 origin x/y/z (y −100, x/z snapped down to 100), u32 ncx, u32 ncz; ntri × {f32 v[3][3], u32 flags}; then ncx·ncz cells (x outer, z inner).
  - Node = u32 n, u32 nx, u32 nz, f32 x, y(0), z, f32 size/10, u32 tri[n], then nx·nz child nodes. 100 m cell with no triangle: (0,0,0,…,10) and no children; else 10×10 children of 10 m. A 10 m node with ≤10 overlapping triangles is a leaf (size field 1.0), else 10×10 leaves of 1 m (size field 0.1). Overlap = exact 2D xz triangle/square test.
  - Triangles (all +y normals): flag 1 = drivable strip along the racing line out to the edges (|lat| ≤ 30, ~2.4 tris per wp); flag 2 = pit-lane strip ±5 m over the speed-limited part (Sochi pit wps 78..162, stalls at 102..152); flag 10 = 2-tri quad at the start of that part, flag 4 = quad at its end [H on the meaning of 10/4: limiter start/end].
- **`object_properties.xml`:** `<PhysicsCollection Version="1.0" />`.

## 8. AC → AMS2 converter (`kn5.py`, `convert.py`, `check_out.py`)

- **`kn5.py`** [V]: reads bahrain.kn5 v5 to EOF: 141 DDS textures, 143 materials, 1215 meshes, 169 dummies.
- **Mirror on Z:**
  - Coordinates [H, strong evidence]: (x, y, z)_AMS2 = (x, y, −z)_AC. Both real tracks run clockwise, but the signed XZ area of the AI loop is +489859 in AC Bahrain and −518442 in AMS2 Sochi.
  - Winding: triangles are reversed so that cross·n > 0 holds, as in Sochi.
  - **Check in game:** the track should not appear mirrored.
- **Meshes:** one .meb per visible, renderable kn5 mesh (AC_* helpers are skipped): 1001 meshes, 994k triangles. Each gets one rz_basic material per kn5 material (txDiffuse only), and DDS files are copied as-is.
  - AT, Tree and Grass shaders get `USE_ALPHATEST` with cull NONE.
  - Blend materials get alpha blending with no z-write.
- **AIW:**
  - Built from `ai\fast_lane.ai` (v7, which has 18 floats of extra data per point; sideLeft/Right are at [5]/[6]).
  - Resampled every 5 m: 1071 waypoints, lap 5357 m.
  - Rotated so that wp0 lies on the AC_TIME_0 line. Sectors come from AC_TIME_1/2.
  - Width = min(left, right) on both sides.
  - Grid comes from AC_START_n and pit boxes from AC_PIT_n.
  - Pit branch from `ai\pit_lane.ai` (a slow, open full lap, 4120 pts): pit section = contiguous run outside the fast-lane edges (+0.5 m) around the AC_PIT stalls, extended until it is within 4 m of the racing line; resampled at 5 m; fork = main wp before its start, join = main wp after its end; one stall spur per AC_PIT_n. Bahrain: 196 pit wps (978 m), fork 1020 → join 148, 24 spurs.
  - AC fast_lane extra [5]/[6] = sideLeft/Right on the +lat/−lat side, lat = rel.x·t.z − rel.z·t.x in AC coordinates [V against the tarmac meshes].
- **triggers.xml:** S/F and S1/S2 boxes between the AC_TIME_n_L/R dummies, using Sochi's CRCs.
- **CSM:** all kn5 meshes named `<digits><KEY>` with KEY in surfaces.ini (+ AC built-in WALL), mirrored + winding reversed, exact-duplicate vertices welded, one cooked mesh per kn5 mesh. Ids (`surf_id`): TARMAC/PITLANE/ROAD/PIT/PAVEMENT 0, KERB/CURB/RUMBLE 10, SAND/GRAVEL 8 (AC SAND = sand.wav gravel trap), GRASS 7, WALL 13 (12 if the name has tyre/tire, 14 if armco/rail/barrier). Other invalid (IS_VALID_TRACK=0) keys and CARPET/OFFTRACK/ROFF follow their AC physics: WAV grass→7, sand/gravel→8, *turf (extraturf.wav)→32, else FRICTION ≥ 0.9 and DIRT_ADDITIVE ≤ 0.05 → 46 RUNOFF_ROAD (paved runoff, e.g. OFFTRACK 0.95, PEN-CNC-*, CONCRETE), otherwise 7 (e.g. OUT 0.85). Keys keep hyphens (PEN-CNC-A); the word prefix (PEN) is kept too for older meshes. Before 22/10 every off-track key was 7 (grass), which made paved runoff unnaturally slippery. Bahrain: 158 meshes, 197k tris, 5.1 MB.
- **GCL:** flag-1 strip from fast_lane sideLeft/Right (clipped 0.5..30 m) every 5 m from the S/F line; flag-2 strip ±5 m over pit-branch points that lie on AC PITLANE physics meshes [H as limiter zone], flag 10/4 quads at its start/end. Bahrain: 2300 tris.
- **Cameras:** from `data\cameras.ini` (POSITION, IN_POINT/OUT_POINT as fractions of fast_lane length): one CTrackingCamData per AC camera with Sochi's template fields; each main wp goes to the camera whose [in,out) holds it (uncovered wps to the nearest camera); one CSphereArea per wp, radius max(6.5, half width + 2) [H: Sochi uses 6.5]. Bahrain: 20 cameras, 1071 areas. AC FOV/DOF values are not carried over.
- **Copied from Sochi as placeholders [H]:** trd (fields replaced), enx, trackproperties.bin, cameraconfig.xml, object_properties, seasonal paks.
- **`check_out.py`** re-reads the output with the independent readers. Current result (Bahrain):
  - 0 CRC/uid errors; every meb parses; all 141 materials and 131 textures and all sgx resources present.
  - AIW loop orientation matches Sochi; grid heading error < 1°; AI line 0.26 m (median) above the nearest road-mesh vertex.
  - Pit branch: pointer topology as in Sochi (0 errors), score monotone, gaps fork→first 8.3 m / last→join 6.7 m, spur wp == PitPos.
  - CSM: 158 meshes, 0 tree errors; vertical rays from the AI line (main every 10th + pit every 5th): 148/148 hit id 0, AI line 0.29 m above the collision surface [H: Sochi's AI line lies on the surface; the game probably snaps].
  - GCL: re-encode identical, all +y normals, 100% of main wps inside flag 1, 40% of pit wps inside flag 2 (= limiter zone 50..127 of 196).
  - Cameras: XML parses, 1071 area refs, all unique and in range, 100% of main wps inside some area.

## 9. Open items / what only the game can confirm

1. Game loads our CSM (own R-tree) and the car drives on it; surface ids give the right grip/sound (ids are the terrain enum, §7; grip of 8/32/46 still to be felt in game, and mTerrain in shared memory should report GRAVEL/ASTROTURF/RUNOFF_ROAD on those areas).
2. AI uses the pit branch (enters at fork 1020, stops at the stalls, rejoins at 148).
3. Track limits / pit limiter from the .gcl; S/F and sector triggers.
4. Cameras switch along the lap.
5. Mirror-on-Z looks right (not mirrored).
6. Names behind trigger CRCs; `trackproperties.bin`; official EXT encryption (optional).
