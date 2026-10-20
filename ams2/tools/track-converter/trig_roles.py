"""Map each trigger shape of a track to lap distance on the AI line (and pit lane), to infer its role."""
import re, sys, math
aiw = open(sys.argv[1], encoding='latin1').read()
trg = open(sys.argv[2], encoding='latin1').read()
wps = [tuple(map(float, m.groups())) for m in re.finditer(r'wp_pos=\(([-\d.]+),([-\d.]+),([-\d.]+)\)', aiw)]
ptrs = [tuple(map(int, m.group(1).split(','))) for m in re.finditer(r'WP_PTRS=\(([^)]*)\)', aiw)]
nmain = int(re.search(r'number_waypoints=(\d+)', aiw).group(1))
# cumulative distance on main loop
cum = [0.0]
for a, b in zip(wps[:nmain], wps[1:nmain]):
    cum.append(cum[-1] + math.dist(a, b))
print('waypoints', len(wps), 'main', nmain, 'loop length %.1f' % cum[-1])
for m in re.finditer(r'<data class="ShapeDesc".*?</data>', trg, re.S):
    blk = m.group()
    g = lambda k: re.search(r'name="%s" data="([^"]*)"' % re.escape(k), blk).group(1)
    pos = tuple(map(float, g('Relative Position').split(';')))
    i = min(range(len(wps)), key=lambda k: math.dist(wps[k], pos))
    print('%-10s crc=%-11s L=%-9s W=%-6s trig=%-5s nearest wp %5d %s dist %.1f m  lapdist %s' % (
        g('Name'), g('Material CRC'), g('Length'), g('Width'), g('Trigger'), i, 'MAIN' if i < nmain else 'PIT ',
        math.dist(wps[i], pos), '%.0f' % cum[i] if i < nmain else '-'))
