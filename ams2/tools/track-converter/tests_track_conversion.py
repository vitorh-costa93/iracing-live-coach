"""Focused source-layout/metadata/AI contract tests. Run: python -I tests_track_conversion.py."""
import contextlib
import importlib.util
import io
import json
from pathlib import Path
import struct
import tempfile
import unittest
from unittest.mock import patch
import xml.etree.ElementTree as ET

import numpy as np

spec = importlib.util.spec_from_file_location('track_convert', Path(__file__).with_name('convert.py'))
convert = importlib.util.module_from_spec(spec)
spec.loader.exec_module(convert)


class SourceLayoutTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)

    def write(self, name, content=''):
        target = self.root / name
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(content, encoding='utf-8')
        return target

    def test_layout_models_with_spaces_and_shared_models_fallback(self):
        self.write('models.ini', '[MODEL_0]\nFILE=shared.kn5\n')
        self.write('shared.kn5')
        self.write('models_gp.ini', '[MODEL_0]\nFILE = "GP road mesh.kn5"\n[MODEL_1]\nFILE=timing.kn5\n')
        self.write('GP road mesh.kn5')
        self.write('timing.kn5')
        self.assertEqual(convert.model_files(self.root, 'gp'), ['GP road mesh.kn5', 'timing.kn5'])
        self.assertEqual(convert.model_files(self.root, 'short'), ['shared.kn5'])
        self.assertEqual(convert.model_files(self.root), ['shared.kn5'])

    def test_partial_models_never_falls_back_to_glob(self):
        self.write('models.ini', '[MODEL_0]\nFILE=present.kn5\n[MODEL_1]\nFILE=missing.kn5\n')
        self.write('present.kn5')
        self.write('unlisted.kn5')
        with self.assertRaisesRegex(ValueError, 'missing.kn5'):
            convert.model_files(self.root)
        self.write('models.ini', '[MODEL_0]\n')
        with self.assertRaisesRegex(ValueError, 'FILE entries'):
            convert.model_files(self.root)

    def test_paths_outside_source_rejected(self):
        for name in ('../other.kn5', r'C:\outside.kn5', r'C:outside.kn5', r'\\server\share\other.kn5'):
            with self.subTest(name=name), self.assertRaises(ValueError):
                convert.source_path(self.root, name)
        for layout in ('..', '../gp', 'gp/sub', r'gp\sub'):
            with self.subTest(layout=layout), self.assertRaises(ValueError):
                convert.source_locations(self.root, layout)

    def test_layout_assets_do_not_use_root_ai_cameras_or_ui(self):
        (self.root / 'gp').mkdir()
        self.write('ai/fast_lane.ai')
        self.write('data/cameras.ini')
        shared_surface = self.write('data/surfaces.ini')
        self.write('ui/preview.png')
        paths = convert.source_locations(self.root, 'gp')
        self.assertEqual(Path(paths['ai']), self.root / 'gp' / 'ai')
        self.assertEqual(Path(paths['cameras']), self.root / 'gp' / 'data' / 'cameras.ini')
        self.assertEqual(Path(paths['ui']), self.root / 'ui' / 'gp')
        self.assertEqual(Path(paths['surfaces']), shared_surface)
        own_surface = self.write('gp/data/surfaces.ini')
        self.assertEqual(Path(convert.source_locations(self.root, 'gp')['surfaces']), own_surface)
        self.assertEqual(Path(convert.source_locations(self.root)['ai']), self.root / 'ai')

    def test_no_models_ini_keeps_legacy_kn5_discovery(self):
        self.write('b.kn5')
        self.write('a.kn5')
        self.write('groove.kn5')
        self.assertEqual(convert.model_files(self.root), ['a.kn5', 'b.kn5'])

    def test_metadata_ui_and_cli_flags(self):
        self.write('ui/gp/ui_track.json', json.dumps(dict(name='Circuit & GP', city='Example', country='Testland', pitboxes='12', version='2.5')))
        meta = convert.source_metadata(self.root / 'ui/gp', 'test_ac', 'gp')
        self.assertEqual(meta['name'], 'Circuit & GP')
        self.assertEqual(meta['city'], 'Example')
        self.assertEqual(meta['country'], 'Testland')
        self.assertEqual(meta['pitboxes'], 12)
        self.assertEqual(meta['version'], '2.5')
        self.assertNotIn('lat', meta)
        args = convert.parse_args(['source', 'id', 'out', 'ref', 'x', '--layout', 'gp', '--name', 'Override', '--lat', '12', '--street', '--require-complete-ai'])
        self.assertEqual(args.name, 'Override')
        self.assertEqual(args.lat, '12')
        self.assertTrue(args.require_complete_ai)
        self.assertEqual(convert.source_metadata(self.root / 'no-ui', 'id')['name'], 'id')

    def test_ac_metadata_allows_literal_multiline_description(self):
        self.write('ui/ui_track.json', '{"name":"Circuit", "description":"first line\nsecond line", "version":"1.0"}')
        self.assertEqual(convert.source_metadata(self.root / 'ui', 'id')['name'], 'Circuit')


TRD_KEYS = ('Name', 'TrackName', 'ShortTrackName', 'ScenegraphFile', 'Year', 'Length', 'Location', 'Country',
            'Track Group', 'Track Description', 'Is Clockwise', 'Track_Location', 'Track_Variation', 'Track_Longitude',
            'Track_Latitude', 'Track_Altitude', 'Track_TimeZone', 'Number Of Turns', 'Track_Climate',
            'Max AI participants', 'Drs Zone 1 Detection Number', 'Drs Zone 2 Detection Number', 'Order Override', 'Version')


class VertexCompactionTests(unittest.TestCase):
    def test_drops_unreferenced_vertices_and_keeps_triangles(self):
        pos = np.arange(30, dtype=np.float32).reshape(10, 3)
        idx = np.array([7, 2, 9, 9, 2, 4])
        keep, remap = convert.compact_vertices(idx)
        self.assertEqual(keep.tolist(), [2, 4, 7, 9])
        np.testing.assert_array_equal(pos[keep][remap], pos[idx])
        self.assertLess(int(remap.max()), len(keep))


class MetadataTests(unittest.TestCase):
    def test_trd_capacity_metadata_escape_and_no_bahrain_defaults(self):
        template = '<root>' + ''.join('<p name="%s" data="old" />' % key for key in TRD_KEYS) + '</root>'
        trd = convert.build_trd(template, 'example_ac', 1000, {'name': 'A & "B"', 'version': '2.0', 'pitboxes': 10}, starts=12, pits=8)
        values = {p.get('name'): p.get('data') for p in ET.fromstring(trd)}
        self.assertEqual(values['Max AI participants'], '7')
        self.assertEqual(values['TrackName'], 'A & "B"')
        self.assertEqual(values['Track_Latitude'], '0')
        self.assertEqual(values['Country'], '')
        self.assertEqual(values['Track_Climate'], 'old')
        self.assertEqual(values['Version'], '2.0')
        self.assertIn('source version 2.0', values['Track Description'])
        self.assertNotIn('Bahrain', trd)
        self.assertNotIn('desert', trd)
        trd = convert.build_trd(template, 'x', 1000, {'pitboxes': 4}, starts=12, pits=8)
        self.assertIn('name="Max AI participants" data="3"', trd)


class SurfaceMatchingTests(unittest.TestCase):
    def test_longest_full_numeric_key_and_legacy_key(self):
        keys = {'ROAD', '1ROAD_MAIN', '1ROAD_MAIN_1', '1PIT', 'WALL'}
        self.assertEqual(convert.match_surface('1road_main_1_SUB', keys), '1ROAD_MAIN_1')
        self.assertEqual(convert.match_surface('2ROAD_m1', keys), 'ROAD')
        self.assertEqual(convert.match_surface('1PiT_12', keys), '1PIT')
        self.assertEqual(convert.match_surface('1WALL_armco', keys), 'WALL')
        self.assertIsNone(convert.match_surface('road_visual', keys))

    def test_surface_semantics_preserve_full_invalid_key(self):
        for key, expected in (('1ROAD_main_1', 0), ('1PIT', 0), ('12WALL_2', 13), ('1CURB_RED', 10), ('3GRASS_out', 7)):
            self.assertEqual(convert.surf_id(key, set()), expected)
        self.assertEqual(convert.surf_id('1UNKNOWN_2', {'1UNKNOWN_2'}), 7)
        self.assertEqual(convert.surf_id('1UNKNOWN_2', {'UNKNOWN'}), 0)

    def test_offtrack_surfaces_follow_ac_physics(self):
        with tempfile.TemporaryDirectory() as folder:
            ini = Path(folder) / 'surfaces.ini'
            ini.write_text('[SURFACE_0]\nKEY=OFFTRACK\nFRICTION=0.95\nDIRT_ADDITIVE=0\nIS_VALID_TRACK=0\n'
                           '[SURFACE_1]\nKEY=PEN-GRS-A\nFRICTION=0.6\nWAV=grass.wav\nDIRT_ADDITIVE=1\nIS_VALID_TRACK=0\n'
                           '[SURFACE_2]\nKEY=CARPET\nFRICTION=0.7\nWAV=extraturf.wav\nDIRT_ADDITIVE=0.1\nIS_VALID_TRACK=1\n'
                           '[SURFACE_3]\nKEY=SAND\nFRICTION=0.8\nWAV=sand.wav ; trap\nDIRT_ADDITIVE=1\nIS_VALID_TRACK=0\n'
                           '[SURFACE_4]\nKEY=OUT\nFRICTION=0.85\nIS_VALID_TRACK=0\n'
                           '[SURFACE_5]\nKEY=PITLANE\nFRICTION=0.95\nIS_VALID_TRACK=0\n')
            bad, props = convert.invalid_keys(str(ini)), convert.surface_props(str(ini))
        self.assertEqual(props['SAND']['WAV'], 'sand.wav')
        expected = {'OFFTRACK': 0, 'PEN-GRS-A': 7, 'CARPET': 32, 'SAND': 8, 'OUT': 7, 'PITLANE': 0, '1GRAVEL': 8, 'GRASS': 7}
        for key, sid in expected.items():
            self.assertEqual(convert.surf_id(key, bad, props), sid, key)
        self.assertEqual(convert.surf_id('OFFTRACK', {'OFFTRACK'}), 7)      # no physics known: previous default

    def test_csm_includes_full_numeric_road_keys(self):
        with tempfile.TemporaryDirectory() as folder:
            ini = Path(folder) / 'surfaces.ini'
            ini.write_text('[SURFACE_0]\nKEY=1ROAD_main_1\nIS_VALID_TRACK=1\n[SURFACE_1]\nKEY=1PIT\nIS_VALID_TRACK=1\n')
            triangle = dict(pos=np.array([[0., 0., 0.], [0., 0., 5.], [5., 0., 0.]]), idx=np.array([0, 1, 2]))
            meshes = [dict(triangle, name=name) for name in ('1ROAD_main_1', '1PIT_2', '1WALL_armco')]
            with patch('csm_write.write_csm', return_value=(b'csm', 3)) as writer, patch.object(convert, 'smooth_kerbs', side_effect=lambda items: items):
                data, count, stats = convert.build_csm({'meshes': meshes}, str(ini))
            self.assertEqual((data, count), (b'csm', 3))
            self.assertEqual([item[2] for item in writer.call_args.args[0]], [0, 0, 14])
            self.assertEqual(stats[('1ROAD_MAIN_1', 0)], 1)

    def test_edges_and_walls_use_same_full_key_matching(self):
        road = dict(name='1ROAD_MAIN_1', mat=0, pos=np.array([[-10., 0., -10.], [-10., 0., 10.], [10., 0., 10.], [10., 0., -10.]]),
                    idx=np.array([0, 1, 2, 0, 2, 3]), visible=True, renderable=True)
        wall = dict(name='12WALL_A', pos=np.array([[3., 0., 0.], [3., 1., 0.], [3., 0., 4.]]), idx=np.array([0, 1, 2]))
        fast = np.array([[0., 0., -2.], [0., 0., 0.], [0., 0., 2.]])
        edges = convert.tarmac_edges({'meshes': [road], 'materials': [{'name': 'neutral'}]}, fast, reach=12, keys={'1ROAD_MAIN_1'})
        self.assertGreaterEqual(edges[1].min(), 9.5)
        walls = convert.wall_free({'meshes': [wall]}, fast, keys={'12WALL_A'})
        self.assertAlmostEqual(walls[1, 0], 3.)


def sample_ai():
    angles = np.linspace(0, 2 * np.pi, 80, endpoint=False)
    fast = np.c_[60 * np.cos(angles), np.zeros(len(angles)), 60 * np.sin(angles)]
    extra = np.ones((len(fast), 18))
    extra[:, 5:7] = 6
    dummies = {}
    for sector, index in enumerate((0, 26, 53)):
        for side in ('L', 'R'):
            matrix = np.eye(4)
            matrix[3, :3] = fast[index]
            dummies['AC_TIME_%d_%s' % (sector, side)] = matrix
    for prefix in ('AC_START_', 'AC_PIT_'):
        for i in range(2):
            matrix = np.eye(4)
            matrix[3, :3] = fast[i]
            dummies[prefix + str(i)] = matrix
    return fast, extra, dummies


class ModelInstanceAndGroundPitTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)

    def fake_kn5(self, data=b'one'):
        dummy = np.eye(4); dummy[3, 0] = 1.
        return dict(textures={'shared.dds': data}, materials=[dict(name='material', tex={'txDiffuse': 'shared.dds'})],
                    meshes=[dict(name='flag', mat=0, pos=np.array([[1., 0., 0.]]), nrm=np.array([[0., 0., 1.]]), tan=np.array([[1., 0., 0.]]))],
                    dummies=[('AC_PIT_0', dummy)])

    def test_instances_apply_yaw_translation_normals_and_dummies_without_mutating_cache(self):
        (self.root / 'flagman.kn5').touch()
        (self.root / 'models.ini').write_text('[MODEL_0]\nFILE=flagman.kn5\nPOSITION=5,2,3 ; comment\nROTATION=90,0,0\n'
                                              '[MODEL_1]\nFILE=flagman.kn5\nPOSITION=-2,0,7\nROTATION=180,0,0\n')
        source = self.fake_kn5()
        with patch.object(convert, 'read_kn5', return_value=source) as reader, contextlib.redirect_stdout(io.StringIO()):
            result = convert.load_all_kn5(self.root)
        self.assertEqual(reader.call_count, 1)
        np.testing.assert_allclose(result['meshes'][0]['pos'][0], [5., 2., 2.], atol=1e-10)
        np.testing.assert_allclose(result['meshes'][1]['pos'][0], [-3., 0., 7.], atol=1e-10)
        np.testing.assert_allclose(result['meshes'][0]['nrm'][0], [1., 0., 0.], atol=1e-10)
        np.testing.assert_allclose(result['dummies'][0][1][3, :3], [5., 2., 2.], atol=1e-10)
        np.testing.assert_equal(source['meshes'][0]['pos'][0], [1., 0., 0.])
        np.testing.assert_equal(source['dummies'][0][1][3, :3], [1., 0., 0.])
        self.assertEqual([m['mat'] for m in result['meshes']], [0, 1])

    def test_distinct_texture_bytes_keep_unique_aliases_across_models(self):
        files = ['same_tail_a.kn5', 'other_tail_a.kn5', 'third_tail_a.kn5']
        for file in files:
            (self.root / file).touch()
        (self.root / 'models.ini').write_text(''.join('[MODEL_%d]\nFILE=%s\n' % (i, file) for i, file in enumerate(files)))
        fixtures = {str(self.root / file): self.fake_kn5(bytes([i])) for i, file in enumerate(files)}
        with patch.object(convert, 'read_kn5', side_effect=lambda path: fixtures[path]), contextlib.redirect_stdout(io.StringIO()):
            result = convert.load_all_kn5(self.root)
        names = [m['tex']['txDiffuse'] for m in result['materials']]
        self.assertEqual(len(set(names)), 3)
        self.assertEqual([result['textures'][name] for name in names], [b'\x00', b'\x01', b'\x02'])

    def test_unverified_pitch_roll_or_invalid_transform_fail(self):
        (self.root / 'flagman.kn5').touch()
        for rotation in ('0,90,0', 'NaN,0,0', '0,0'):
            (self.root / 'models.ini').write_text('[MODEL_0]\nFILE=flagman.kn5\nROTATION=' + rotation)
            with self.subTest(rotation=rotation), self.assertRaises(ValueError):
                convert.model_files(self.root)

    def ground_source(self, stacked=False):
        triangle = np.array([[0., 0., 0.], [10., 2., 0.], [0., 0., 10.]])
        mesh = dict(name='1ROAD_MAIN', pos=triangle, idx=np.array([0, 1, 2]))
        meshes = [mesh]
        if stacked:
            meshes.append(dict(mesh, pos=triangle + [0., 1., 0.]))
        path = self.root / 'surfaces.ini'; path.write_text('[SURFACE_0]\nKEY=ROAD\nIS_VALID_TRACK=1\n')
        matrix = np.eye(4); matrix[:3, :3] = [[0., 0., -1.], [0., 1., 0.], [1., 0., 0.]]; matrix[3, :3] = [2., 2., 2.]
        return {'meshes': meshes}, {'AC_PIT_0': matrix, 'AC_PIT_1': matrix.copy()}, str(path)

    def test_ground_all_pits_preserves_xz_orientation_and_source(self):
        source, dummies, path = self.ground_source()
        with contextlib.redirect_stdout(io.StringIO()):
            result = convert.ground_pit_dummies(source, dummies, path)
        for name in dummies:
            self.assertAlmostEqual(result[name][3, 1], 0.7)
            np.testing.assert_equal(result[name][3, [0, 2]], dummies[name][3, [0, 2]])
            np.testing.assert_equal(result[name][:3], dummies[name][:3])
            self.assertEqual(dummies[name][3, 1], 2.)
        args = convert.parse_args(['src', 'id', 'out', 'ref', 'x', '--ground-pits'])
        self.assertTrue(args.ground_pits)

    def test_ground_missing_ambiguous_or_excessive_floor_fails(self):
        source, dummies, path = self.ground_source(stacked=True)
        with self.assertRaisesRegex(ValueError, 'ambiguous stacked'):
            convert.ground_pit_dummies(source, dummies, path)
        source, dummies, path = self.ground_source()
        dummies['AC_PIT_0'][3, 1] = 99.
        with self.assertRaisesRegex(ValueError, 'excessive correction'):
            convert.ground_pit_dummies(source, dummies, path)
        dummies['AC_PIT_0'][3, 0] = 20.
        with self.assertRaisesRegex(ValueError, 'no finite asphalt floor'):
            convert.ground_pit_dummies(source, dummies, path)
        source['meshes'][0]['name'] = '1GRASS'
        with self.assertRaisesRegex(ValueError, 'no asphalt collision'):
            convert.ground_pit_dummies(source, dummies, path)


class PitLimiterAnchorTests(unittest.TestCase):
    def setUp(self):
        self.pit = np.c_[np.zeros(21), np.zeros(21), np.arange(21) * 5.]

    def mesh(self, role, z, x=3.):
        centre = np.array([x, 2., z])
        return dict(name='pitlight%s_sub0' % role, pos=np.array([centre - 0.1, centre + 0.1]))

    def test_explicit_entry_exit_define_zone_and_gcl_flags(self):
        from gcl import read_gcl
        with contextlib.redirect_stdout(io.StringIO()) as output:
            zone = convert.pit_light_zone(self.pit, [self.mesh('in', 20), self.mesh('out', 80)])
        self.assertEqual(zone, (4, 16))
        self.assertIn('source=pitlightin/out indices=4:16', output.getvalue())
        fast, extra, _ = sample_ai()
        data, _, returned = convert.build_gcl(fast, extra, fast[0], self.pit, None, limiter_zone=zone)
        flags = read_gcl(data)['tris']['f'].tolist()
        self.assertEqual(returned, zone)
        self.assertEqual(flags.count(2), 24)
        self.assertEqual(flags.count(10), 2)
        self.assertEqual(flags.count(4), 2)

    def test_reversed_or_offlane_or_incomplete_anchors_fail(self):
        for meshes, error in (([self.mesh('in', 80), self.mesh('out', 20)], 'reversed'),
                              ([self.mesh('in', 20, x=6), self.mesh('out', 80)], 'outside pit strip'),
                              ([self.mesh('in', 20)], 'need both')):
            with self.subTest(error=error), contextlib.redirect_stdout(io.StringIO()):
                with self.assertRaisesRegex(ValueError, error):
                    convert.pit_light_zone(self.pit, meshes)

    def test_ambiguous_multiple_entry_stations_fail(self):
        meshes = [self.mesh('in', 20), self.mesh('in', 50), self.mesh('out', 80)]
        with self.assertRaisesRegex(ValueError, 'multiple stations'):
            convert.pit_light_zone(self.pit, meshes)

    def test_no_anchors_does_not_invent_a_zone(self):
        self.assertIsNone(convert.pit_light_zone(self.pit, []))


class AIValidationTests(unittest.TestCase):
    def test_pit_outside_uses_correct_asymmetric_side_width(self):
        # Straight main lane heading +z: +x is col5, -x is col6.
        fast = np.array([[0., 0., float(z)] for z in range(8)])
        extra = np.zeros((len(fast), 18)); extra[:, 5] = 12.; extra[:, 6] = 2.7
        probes = np.array([[-10., 0., 3.], [10., 0., 3.], [-2., 0., 3.], [13., 0., 3.]])
        _, outside = convert.pit_section(fast, extra, probes)
        self.assertEqual(outside.tolist(), [True, False, False, True])
        extra[:, 5], extra[:, 6] = 2.7, 12.
        _, outside = convert.pit_section(fast, extra, probes)
        self.assertEqual(outside.tolist(), [False, True, False, True])

    def test_asymmetric_pit_branch_builds_and_connects_to_main(self):
        from aiw_pit import parse_aiw, tup
        from validation_contracts import pit_endpoint_errors
        fast, extra, dummies = sample_ai()
        extra[:, 5], extra[:, 6] = 12., 2.7
        # Circle heading: +perp is outward, so inward pit belongs to narrow col6.
        pitlane = fast.copy(); pitlane[20:51, [0, 2]] *= 0.82
        matrix = np.eye(4); matrix[3, :3] = pitlane[35]
        dummies['AC_PIT_0'] = matrix
        matrix2 = matrix.copy(); matrix2[3, :3] = pitlane[36]
        dummies['AC_PIT_1'] = matrix2
        branch = convert.build_pit(fast, extra, pitlane, [('AC_PIT_0', matrix), ('AC_PIT_1', matrix2)])
        self.assertIsNotNone(branch)
        with contextlib.redirect_stdout(io.StringIO()):
            aiw, info = convert.build_aiw(fast, extra, dummies, 'pit_test', pit=branch)
        waypoints = [w for w in parse_aiw(aiw.split('[Waypoint]')[1]) if 'wp_pos' in w]
        branches = np.array([int(tup(w['wp_branchID'])[0]) for w in waypoints])
        pointers = np.array([tup(w['WP_PTRS']) for w in waypoints], dtype=int)
        self.assertEqual(pit_endpoint_errors(branches, pointers), [])
        self.assertGreater(info['npit'], 2)

    def test_wide_runoff_still_allows_explicit_separated_pit(self):
        fast, extra, _ = sample_ai()
        extra[:, 5:7] = 30.
        pitlane = fast.copy(); pitlane[20:51, [0, 2]] *= 0.82
        matrix = np.eye(4); matrix[3, :3] = pitlane[35]
        with contextlib.redirect_stdout(io.StringIO()) as output:
            branch = convert.build_pit(fast, extra, pitlane, [('AC_PIT_0', matrix)])
        self.assertIsNotNone(branch)
        self.assertGreater(len(branch), 3)
        self.assertIn('outside_width=False', output.getvalue())
        # A pit file that just retraces the main racing line does not establish a branch.
        with contextlib.redirect_stdout(io.StringIO()):
            self.assertIsNone(convert.build_pit(fast, extra, fast, [('AC_PIT_0', matrix)]))

    def test_complete_ai_requires_extras_pit_and_dummies(self):
        fast, extra, dummies = sample_ai()
        convert.validate_ai_inputs(fast, extra, dummies, fast, True)
        for extras, pit in ((None, fast), (extra, None)):
            with self.assertRaisesRegex(ValueError, 'Complete AI'):
                convert.validate_ai_inputs(fast, extras, dummies, pit, True)
        for name in ('AC_TIME_1_L', 'AC_START_0', 'AC_PIT_0'):
            bad = dict(dummies)
            del bad[name]
            with self.subTest(name=name), self.assertRaises(ValueError):
                convert.validate_ai_inputs(fast, extra, bad)

    def test_nonfinite_ai_and_widths_rejected(self):
        fast, extra, dummies = sample_ai()
        extra[0, 5] = float('nan')
        with self.assertRaisesRegex(ValueError, 'finite positive'):
            convert.validate_ai_inputs(fast, extra, dummies)
        fast[0, 0] = float('inf')
        with self.assertRaisesRegex(ValueError, 'finite 3D'):
            convert.validate_ai_inputs(fast, None, dummies)

    def test_read_ai_reports_truncation_and_extras(self):
        fast, extra, _ = sample_ai()
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / 'fast_lane.ai'
            path.write_bytes(b'bad')
            with self.assertRaisesRegex(ValueError, 'Truncated'):
                convert.read_ai(path)
            data = struct.pack('<4i', 7, len(fast), 0, 0)
            data += b''.join(struct.pack('<4fi', *point, 0, i) for i, point in enumerate(fast))
            path.write_bytes(data + struct.pack('<i', len(fast)) + extra.astype('<f4').tobytes())
            points, widths = convert.read_ai(path)
            self.assertEqual(points.shape, (80, 3))
            self.assertEqual(widths.shape, (80, 18))
            path.write_bytes(data + struct.pack('<i', 0))
            self.assertIsNone(convert.read_ai(path)[1])

    def test_solver_and_aiw_emit_finite_clearance_diagnostics(self):
        fast, extra, dummies = sample_ai()
        with contextlib.redirect_stdout(io.StringIO()) as output:
            aiw, info = convert.build_aiw(fast, extra, dummies, 'test')
        self.assertIn('converged=True', output.getvalue())
        self.assertIn('AI margins:', output.getvalue())
        self.assertGreaterEqual(info['margin_left'], 0.999)
        self.assertGreaterEqual(info['margin_right'], 0.999)
        self.assertEqual((info['starts'], info['pits']), (2, 2))
        self.assertNotRegex(aiw.lower(), r'\b(?:nan|inf)\b')

    def test_infeasible_wall_corridor_fails(self):
        fast, extra, dummies = sample_ai()
        with self.assertRaisesRegex(ValueError, 'Infeasible AI clearance'):
            convert.build_aiw(fast, extra, dummies, 'test', wallf=np.ones((len(fast), 2)))

    def test_nonfinite_solver_result_fails(self):
        fast, _, _ = sample_ai()
        with patch('scipy.sparse.linalg.spsolve', return_value=np.full(len(fast), np.nan)):
            with self.assertRaisesRegex(ValueError, 'non-finite offsets'):
                convert.min_curvature_offsets(fast, np.ones_like(fast), -np.ones(len(fast)), np.ones(len(fast)), np.zeros(len(fast)))

    def narrow_corridor(self):
        angles = np.linspace(0, 2 * np.pi, 160, endpoint=False)
        radius = 60 + 9 * np.sin(5 * angles) + 3 * np.sin(11 * angles)
        points = np.c_[radius * np.cos(angles), np.zeros(len(angles)), radius * np.sin(angles)]
        tangent = np.roll(points, -1, axis=0) - np.roll(points, 1, axis=0)
        tangent /= np.linalg.norm(tangent, axis=1)[:, None]
        perp = np.c_[tangent[:, 2], np.zeros(len(angles)), -tangent[:, 0]]
        lo = -0.4 - 0.15 * np.sin(3 * angles)
        hi = 0.35 + 0.12 * np.cos(4 * angles)
        return points, perp, lo, hi, np.zeros(len(angles))

    def test_narrow_curves_fallback_meets_independent_kkt(self):
        points, perp, lo, hi, initial = self.narrow_corridor()
        with contextlib.redirect_stdout(io.StringIO()) as output:
            offsets = convert.min_curvature_offsets(points, perp, lo, hi, initial, rounds=1)
        self.assertIn('method=L-BFGS-B', output.getvalue())
        self.assertIn('converged=True', output.getvalue())
        self.assertTrue(np.all((offsets >= lo) & (offsets <= hi)))
        # Independently differentiate ||D2(points + offset*perp)||^2 / 2 + regularisation.
        line = points + offsets[:, None] * perp
        d2_line = np.roll(line, 1, axis=0) - 2 * line + np.roll(line, -1, axis=0)
        d4_line = np.roll(d2_line, 1, axis=0) - 2 * d2_line + np.roll(d2_line, -1, axis=0)
        gradient = np.sum(d4_line * perp, axis=1) + convert.OPT_REG ** 2 * (offsets - initial)
        projected = offsets - np.clip(offsets - gradient, lo, hi)
        self.assertLessEqual(np.max(np.abs(projected)), 1e-5)
        original_d2 = np.roll(points, 1, axis=0) - 2 * points + np.roll(points, -1, axis=0)
        self.assertLess(np.sum(d2_line ** 2) + convert.OPT_REG ** 2 * np.sum(offsets ** 2), np.sum(original_d2 ** 2))

    def test_out_in_out_line_uses_full_width_in_every_corner(self):
        # rounded square, 200 m straights, R 30 m corners, 5 m spacing, corridor +-6 m
        pts, R, S = [], 30.0, 200.0
        centres = [(S / 2, S / 2), (-S / 2, S / 2), (-S / 2, -S / 2), (S / 2, -S / 2)]
        for q, (cx, cz) in enumerate(centres):
            a0 = q * np.pi / 2
            p0 = np.array([cx + R * np.cos(a0), cz + R * np.sin(a0)]); d = np.array([-np.sin(a0), np.cos(a0)])
            pts += [p0 - d * (S - t) for t in np.arange(0, S, 5.0)]
            pts += [np.array([cx + R * np.cos(a0 + u / R), cz + R * np.sin(a0 + u / R)]) for u in np.arange(0, R * np.pi / 2, 5.0)]
        W = np.array([[p[0], 0.0, p[1]] for p in pts])
        t = np.roll(W, -1, 0) - np.roll(W, 1, 0); t /= np.linalg.norm(t, axis=1)[:, None]
        pw = np.c_[t[:, 2], np.zeros(len(W)), -t[:, 0]]
        lo, hi = -6.0 * np.ones(len(W)), 6.0 * np.ones(len(W))
        with contextlib.redirect_stdout(io.StringIO()):
            d = convert.racing_line_oio(W, pw, lo, hi, np.zeros(len(W)))
        self.assertTrue(np.all((d >= lo - 1e-9) & (d <= hi + 1e-9)))
        inner = np.sign(np.sum(pw * -W, axis=1))                 # toward the centre of the square
        n_arc, n_str = int(np.ceil(R * np.pi / 2 / 5.0)), int(S / 5.0)
        for q in range(4):
            a = q * (n_str + n_arc) + n_str; b = a + n_arc - 1
            din = lambda i: 6.0 - inner[i % len(W)] * d[i % len(W)]
            dout = lambda i: 6.0 + inner[i % len(W)] * d[i % len(W)]
            self.assertLessEqual(min(din(i) for i in range(a, b + 1)), 0.31)          # inner kerb at the apex
            self.assertLessEqual(min(dout(i) for i in range(a - 12, a + 1)), 0.41)    # outer edge before turn-in
            self.assertLessEqual(min(dout(i) for i in range(b, b + 13)), 0.41)        # outer edge on the exit
            apex = min(range(a, b + 1), key=din)
            self.assertGreaterEqual((apex - a) / max(b - a, 1), 0.4)                    # late apex

    def test_kerb_start_stops_the_corridor_where_kerb_collision_begins(self):
        fast = np.array([[0.0, 0.0, z] for z in np.arange(0.0, 100.0, 5.0)])
        quad = lambda x0, x1: dict(pos=np.array([[x0, 0, -10], [x1, 0, -10], [x1, 0, 110], [x0, 0, 110]], float),
                                   idx=np.array([0, 1, 2, 0, 2, 3]))
        k = dict(meshes=[dict(name='1KERB_IN', **quad(-4.0, -3.0)), dict(name='1ROAD', **quad(-8.0, 8.0))])
        kb = convert.kerb_start(k, fast, keys={'KERB', 'ROAD'})[1:-1]      # open test line: end tangents wrap around
        # perp of a +z line is (+1, 0, 0) for col 5 and -x for col 6: kerb at 3 m on the -x side only, conservative by one cell
        self.assertTrue(np.all(kb[:, 0] == 30.0))
        self.assertTrue(np.all((kb[:, 1] >= 2.5) & (kb[:, 1] <= 3.0)))

    @staticmethod
    def rounded_square(R=30.0, S=200.0):
        pts = []
        centres = [(S / 2, S / 2), (-S / 2, S / 2), (-S / 2, -S / 2), (S / 2, -S / 2)]
        for q, (cx, cz) in enumerate(centres):
            a0 = q * np.pi / 2
            p0 = np.array([cx + R * np.cos(a0), cz + R * np.sin(a0)]); d = np.array([-np.sin(a0), np.cos(a0)])
            pts += [p0 - d * (S - t) for t in np.arange(0, S, 5.0)]
            pts += [np.array([cx + R * np.cos(a0 + u / R), cz + R * np.sin(a0 + u / R)]) for u in np.arange(0, R * np.pi / 2, 5.0)]
        W = np.array([[p[0], 0.0, p[1]] for p in pts])
        t = np.roll(W, -1, 0) - np.roll(W, 1, 0); t /= np.linalg.norm(t, axis=1)[:, None]
        return W, np.c_[t[:, 2], np.zeros(len(W)), -t[:, 0]]

    def test_lap_time_gradient_matches_estimate_and_finite_differences(self):
        W, pw = self.rounded_square()
        d = 2.0 * np.sin(np.arange(len(W)) * 0.07)
        t, g = convert.lap_time_grad(W, pw, d)
        self.assertAlmostEqual(t, convert.est_laptime(W + d[:, None] * pw), places=8)
        for i in (3, 40, 47, 90, len(W) - 5):
            e = np.zeros(len(W)); e[i] = 1e-6
            fd = (convert.est_laptime(W + (d + e)[:, None] * pw) - t) / 1e-6
            self.assertAlmostEqual(g[i], fd, delta=1e-4 + 1e-3 * abs(fd))

    def test_zebra_line_goes_outer_to_kerb_at_a_late_apex_and_is_never_slower(self):
        W, pw = self.rounded_square()
        n = len(W); lo, hi, ac = -6.0 * np.ones(n), 6.0 * np.ones(n), np.zeros(n)
        with contextlib.redirect_stdout(io.StringIO()):
            d = convert.racing_line(W, pw, lo, hi, ac, mode='zebra')
            fast = convert.racing_line(W, pw, lo, hi, ac, mode='fastest')
        self.assertTrue(np.all((d >= lo - 1e-9) & (d <= hi + 1e-9)))
        lap = lambda x: convert.est_laptime(W + x[:, None] * pw)
        self.assertLessEqual(lap(d), lap(fast) + 1e-9)
        inner = np.sign(np.sum(pw * -W, axis=1))
        n_arc, n_str = int(np.ceil(30.0 * np.pi / 2 / 5.0)), 40
        for q in range(4):
            a = q * (n_str + n_arc) + n_str; b = a + n_arc - 1
            din = lambda i: 6.0 - inner[i % n] * d[i % n]
            apex = max(range(a, b + 1), key=lambda i: (din(i) <= 0.16, i))
            self.assertLessEqual(din(apex), 0.16)                                   # touches the kerb-limited bound
            self.assertGreaterEqual((apex - a) / max(b - a, 1), 0.4)                # late apex
            self.assertGreaterEqual(din(apex - 5), 1.0)                             # no creeping along the kerb 25 m before it
            self.assertLessEqual(min(12.0 - din(i) for i in range(a - 16, a + 1)), 0.5)   # outer edge before turn-in

    def test_default_racing_line_never_slower_than_source(self):
        pts, R, S = [], 30.0, 200.0
        centres = [(S / 2, S / 2), (-S / 2, S / 2), (-S / 2, -S / 2), (S / 2, -S / 2)]
        for q, (cx, cz) in enumerate(centres):
            a0 = q * np.pi / 2
            p0 = np.array([cx + R * np.cos(a0), cz + R * np.sin(a0)]); d = np.array([-np.sin(a0), np.cos(a0)])
            pts += [p0 - d * (S - t) for t in np.arange(0, S, 5.0)]
            pts += [np.array([cx + R * np.cos(a0 + u / R), cz + R * np.sin(a0 + u / R)]) for u in np.arange(0, R * np.pi / 2, 5.0)]
        W = np.array([[p[0], 0.0, p[1]] for p in pts])
        t = np.roll(W, -1, 0) - np.roll(W, 1, 0); t /= np.linalg.norm(t, axis=1)[:, None]
        pw = np.c_[t[:, 2], np.zeros(len(W)), -t[:, 0]]
        lo, hi, ac = -6.0 * np.ones(len(W)), 6.0 * np.ones(len(W)), np.zeros(len(W))
        with contextlib.redirect_stdout(io.StringIO()):
            d = convert.racing_line(W, pw, lo, hi, ac, mode='fastest')
            late = convert.racing_line(W, pw, lo, hi, ac, mode='late_apex')
        self.assertTrue(np.all((d >= lo - 1e-9) & (d <= hi + 1e-9)))
        lap = lambda x: convert.est_laptime(W + x[:, None] * pw)
        self.assertLessEqual(lap(d), lap(ac) + 1e-9)      # optimised line only kept when it beats the source line
        self.assertLessEqual(lap(d), lap(late) + 1e-6)
        with self.assertRaisesRegex(ValueError, 'Unknown racing-line mode'):
            convert.racing_line(W, pw, lo, hi, ac, mode='bogus')

    def test_real_fallback_iteration_limit_remains_an_error(self):
        with contextlib.redirect_stdout(io.StringIO()):
            with self.assertRaisesRegex(ValueError, 'did not converge: KKT'):
                convert.min_curvature_offsets(*self.narrow_corridor(), rounds=1, fallback_maxiter=1)

    def test_optimizer_success_flag_cannot_bypass_kkt(self):
        from types import SimpleNamespace
        points, perp, lo, hi, initial = self.narrow_corridor()
        fake_result = SimpleNamespace(x=initial, success=True, nit=1, message='claimed convergence')
        with patch('scipy.optimize.minimize', return_value=fake_result), contextlib.redirect_stdout(io.StringIO()):
            with self.assertRaisesRegex(ValueError, 'did not converge: KKT'):
                convert.min_curvature_offsets(points, perp, lo, hi, initial, rounds=1)

    def test_source_width_bottleneck_race_stays_inside_gcl(self):
        from aiw_pit import parse_aiw, tup
        from gcl import read_gcl, sat_xz
        fast, extra, dummies = sample_ai()
        extra[40, 6] = 0.55
        with contextlib.redirect_stdout(io.StringIO()):
            aiw, info = convert.build_aiw(fast, extra, dummies, 'bottleneck')
        data, _, _ = convert.build_gcl(fast, extra, fast[0], None, None)
        gcl = read_gcl(data)
        triangles = gcl['tris']['v'][:, :, [0, 2]].astype(float)
        ids = np.flatnonzero(gcl['tris']['f'] == 1)
        waypoints = [w for w in parse_aiw(aiw.split('[Waypoint]')[1]) if 'wp_pos' in w]
        for index, waypoint in enumerate(waypoints):
            race = np.array(tup(waypoint['wp_pos'])) + tup(waypoint['wp_path'])[0] * np.array(tup(waypoint['wp_perp']))
            self.assertGreater(len(sat_xz(triangles, ids, race[0] - 0.01, race[2] - 0.01, 0.02)), 0, 'Outside GCL at waypoint %d' % index)


if __name__ == '__main__':
    unittest.main()
