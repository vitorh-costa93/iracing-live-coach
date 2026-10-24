"""Terrain materials: grass/gravel colour, alpha, mips and specular as AMS2 rz_basic will show them.

Regression for Hungaroring 1988 (grass black: premultiplied mips of an alpha-0 diffuse) and Magny-Cours 1992/2026
(gravel white: AC macro diffuse instead of the detail texture; unmasked ksSpecular 1.0).
"""
import importlib.util
import io
from pathlib import Path
import re
import struct
import unittest

import numpy as np
from PIL import Image

spec = importlib.util.spec_from_file_location('convert', Path(__file__).with_name('convert.py'))
c = importlib.util.module_from_spec(spec); spec.loader.exec_module(c)


def dds(rgba, size=64, fmt='DXT5', mips=1):
    im = Image.new('RGBA', (size, size), tuple(rgba))
    b = io.BytesIO(); im.save(b, 'DDS', pixel_format=fmt)
    d = bytearray(b.getvalue()); struct.pack_into('<I', d, 28, mips)
    return bytes(d)


def level_mean(data, level):
    h, w, _, _, mips = struct.unpack_from('<IIIII', data, 12)
    off = 128
    for lv in range(level):
        off += max(1, (max(w >> lv, 1) + 3) // 4) * max(1, (max(h >> lv, 1) + 3) // 4) * 16
    head = bytearray(data[:128]); lw, lh = max(w >> level, 1), max(h >> level, 1)
    struct.pack_into('<II', head, 12, lh, lw); struct.pack_into('<I', head, 28, 1)
    return np.asarray(Image.open(io.BytesIO(bytes(head) + data[off:])).convert('RGBA')).reshape(-1, 4).mean(0)


def mat(name, shader='ksPerPixelMultiMap_NMDetail', tex=None, blend=0, at=0, props=None):
    return dict(name=name, mname=name.upper(), shader=shader, tex=tex or {}, blend=blend, alphaTested=at, props=props or {})


class TerrainTests(unittest.TestCase):
    def setUp(self):
        c.AVAIL = None; c.TEXDATA = {}; c._TEX_MEAN.clear()

    def tearDown(self):
        c.AVAIL = None; c.TEXDATA = None; c._TEX_MEAN.clear()

    def test_mips_keep_colour_under_alpha_mask(self):
        # Hungaroring RATL_MAINGRASS: green RGB, alpha 0 (AC detail mask), single mip -> every rebuilt level was black
        out = c.full_mips(dds((83, 102, 63, 0)))
        self.assertEqual(struct.unpack_from('<I', out, 28)[0], 7)
        for lv in (1, 3, 6):
            self.assertGreater(level_mean(out, lv)[1], 90, 'mip %d lost its colour' % lv)
        opaque = c.terrain_texture(out, 'grass')
        self.assertEqual(level_mean(opaque, 0)[3], 255)
        self.assertGreater(level_mean(opaque, 4)[1], 90)

    def test_gravel_uses_detail_when_diffuse_is_white_macro_map(self):
        # Magny-Cours GRVL_SANDTRAP: sand_base 227/222/206 alpha 7, txDetail trap_detail 144/136/121
        c.TEXDATA = {'sand_base.dds': dds((227, 222, 206, 7)), 'trap_detail.dds': dds((144, 136, 121, 140)),
                     'sand_base_map.dds': dds((135, 0, 0, 255))}
        m = mat('GRVL_SANDTRAP', tex={'txDiffuse': 'sand_base.dds', 'txDetail': 'trap_detail.dds', 'txMaps': 'sand_base_map.dds'},
                props={'ksSpecular': [0.3]})
        self.assertEqual(c.terrain_kind(m), 'gravel')
        self.assertEqual(c.tex_of(m), 'trap_detail.dds')
        self.assertEqual(c.detail_kind(m), 'gravel')
        self.assertFalse(c.is_asphalt(m))
        spec_v = float(re.search(r'globalSpecularFactor.*?v="([^"]+)"', c.make_mtx(m, 't', 'X.DDS'), re.S).group(1))
        self.assertAlmostEqual(spec_v, 0.3 * 135 / 255, places=2)

    def test_grass_multilayer_is_not_asphalt_and_tiles_green_detail(self):
        # Magny-Cours GRASS_FILLER: pure ksMultilayer, grass-b 239/241/227 was toned to grey as 'asphalt'
        c.TEXDATA = {'grass-b.dds': dds((239, 241, 227, 47)), 'grass-detail18.dds': dds((81, 92, 49, 10))}
        m = mat('GRASS_FILLER', 'ksMultilayer', {'txDiffuse': 'grass-b.dds', 'txDetailR': 'grass-detail18.dds'})
        self.assertFalse(c.is_asphalt(m))
        self.assertEqual(c.tex_of(m), 'grass-detail18.dds')

    def test_diffuse_kept_when_it_already_fits(self):
        # Hungaroring GRASS: green diffuse, beige detail -> keep the diffuse; Magny-Cours 2026 GRVL_01 gravel_dif
        c.TEXDATA = {'g.dds': dds((83, 102, 63, 0)), 'det.dds': dds((152, 144, 128, 95)), 'gv.dds': dds((161, 147, 122, 255)),
                     'map.dds': dds((0, 0, 0, 255))}
        g = mat('GRASS', tex={'txDiffuse': 'g.dds', 'txDetail': 'det.dds'})
        self.assertEqual(c.tex_of(g), 'g.dds'); self.assertIsNone(c.detail_kind(g))
        gv = mat('GRVL_01', tex={'txDiffuse': 'gv.dds', 'txMaps': 'map.dds'}, props={'ksSpecular': [1.0]})
        self.assertEqual(c.tex_of(gv), 'gv.dds')
        spec_v = float(re.search(r'globalSpecularFactor.*?v="([^"]+)"', c.make_mtx(gv, 't', 'X.DDS'), re.S).group(1))
        self.assertLessEqual(spec_v, 0.01)    # was 1.0: white sheen over the traps

    def test_spec_capped_without_map_and_non_terrain_untouched(self):
        g = mat('GRASSOUTER', 'ksPerPixel', props={'ksSpecular': [0.8]})
        w = mat('TWALL_GREEN', 'ksPerPixel', props={'ksSpecular': [0.8]})
        sp = lambda m: float(re.search(r'globalSpecularFactor.*?v="([^"]+)"', c.make_mtx(m, 't', 'X.DDS'), re.S).group(1))
        self.assertAlmostEqual(sp(g), c.TERRAIN_SPEC_MAX, places=5)
        self.assertAlmostEqual(sp(w), 0.8, places=5)
        self.assertIsNone(c.terrain_kind(w))
        self.assertIsNone(c.terrain_kind(mat('GRASS_DECAL', 'ksPerPixelAlpha', blend=1)))

    def test_opaque_ksgrass_ground_and_named_by_texture(self):
        # Sepang 'grass ext' (ksGrass, grey diffuse, green txVariation); COTA 'del_tarmac_a!7' (ksMultilayer, grass.dds)
        c.TEXDATA = {'grass_ext.dds': dds((152, 152, 152, 255)), 'var.dds': dds((82, 86, 52, 7)), 'tuft.dds': dds((119, 126, 84, 122)),
                     'grass.dds': dds((208, 209, 208, 255)), 'gd.dds': dds((72, 78, 30, 255))}
        s = mat('grass ext', 'ksGrass', {'txDiffuse': 'grass_ext.dds', 'txVariation': 'var.dds'}, at=1)
        self.assertEqual(c.terrain_kind(s), 'grass'); self.assertEqual(c.tex_of(s), 'var.dds')
        self.assertIsNone(c.terrain_kind(mat('3Dgrass', 'ksGrass', {'txDiffuse': 'tuft.dds'}, at=1)))
        d = mat('del_tarmac_a!7', 'ksMultilayer', {'txDiffuse': 'grass.dds', 'txDetailR': 'gd.dds'})
        self.assertEqual(c.terrain_kind(d), 'grass'); self.assertEqual(c.tex_of(d), 'gd.dds'); self.assertFalse(c.is_asphalt(d))

    def test_texture_word_wins_over_material_word(self):
        # Zandvoort GRASS_INNER3 is a sand trap (sand_base + trap_detail); GRASS_SAND paints grass_natural
        c.TEXDATA = {'sand_base.dds': dds((227, 222, 206, 7)), 'trap_detail.dds': dds((144, 136, 121, 140)),
                     'grass_natural.dds': dds((55, 58, 37, 134))}
        g = mat('GRASS_INNER3', tex={'txDiffuse': 'sand_base.dds', 'txDetail': 'trap_detail.dds'})
        self.assertEqual(c.terrain_kind(g), 'gravel'); self.assertEqual(c.tex_of(g), 'trap_detail.dds')
        # Sepang 'sand' (ksGrass on grass_brd_1b, sand in txVariation) stays gravel: the named kind has a fitting texture
        c.TEXDATA.update({'grass_brd_1b.dds': dds((213, 212, 212, 255)), 'sand.dds': dds((119, 111, 99, 255))})
        s = mat('sand', 'ksGrass', {'txDiffuse': 'grass_brd_1b.dds', 'txVariation': 'sand.dds'}, at=1)
        self.assertEqual(c.terrain_kind(s), 'gravel'); self.assertEqual(c.tex_of(s), 'sand.dds')
        self.assertTrue(re.search(c.GROUND_RX, 'CONC_SMOOTH_GREY_LIGHT')); self.assertTrue(re.search(c.GROUND_RX, 'PITFLOOR'))

    def test_green_multilayer_background_is_not_asphalt(self):
        c.TEXDATA = {'bg.dds': dds((76, 90, 67, 255)), 'road.dds': dds((98, 98, 98, 224))}
        c.AVAIL = set(c.TEXDATA)    # no tarmac tile in this kn5 (Magny-Cours 1992)
        self.assertFalse(c.is_asphalt(mat('Background', 'ksMultilayer', {'txDiffuse': 'bg.dds'})))
        self.assertTrue(c.is_asphalt(mat('ROADX', 'ksMultilayer', {'txDiffuse': 'road.dds'})))


if __name__ == '__main__':
    unittest.main()
