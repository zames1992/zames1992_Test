#!/usr/bin/env python3
"""Render the Hoodie rig (src/HoodieCompanion/Assets/rig.json) to SVG/PNG.

Used to validate the cutout rig against docs/reference.png without Windows.

  python3 tools/render_rig.py out.svg                 # neutral pose
  python3 tools/render_rig.py out.svg pose.json       # pose: {"group": {"rot": deg, "dx": px, "dy": px, "sx": 1, "sy": 1}}
  python3 tools/render_rig.py out.svg --hidden item   # hide groups
"""
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
RIG = os.path.join(HERE, "..", "src", "HoodieCompanion", "Assets", "rig.json")


def group_transform(rig, pose, name):
    g = rig["groups"][name]
    px, py = g["pivot"]
    p = pose.get(name, {})
    own = (
        f"translate({px + p.get('dx', 0)} {py + p.get('dy', 0)}) "
        f"rotate({p.get('rot', 0)}) scale({p.get('sx', 1)} {p.get('sy', 1)}) "
        f"translate({-px} {-py})"
    )
    parent = g.get("parent")
    return (group_transform(rig, pose, parent) + " " + own) if parent else own


def render(rig, pose, hidden, flip=False):
    x0, y0, x1, y1 = 0, 0, 559, 895
    out = [f'<svg xmlns="http://www.w3.org/2000/svg" width="{x1}" height="{y1}" viewBox="{x0} {y0} {x1} {y1}">']
    if flip:
        out.append(f'<g transform="translate({2 * rig["root"][0]} 0) scale(-1 1)">')
    for part in rig["parts"]:
        grp = part["group"]
        if grp in hidden or grp.split(".")[0] in hidden:
            continue
        stroke = part.get("stroke", rig["outline"])
        sw = part.get("strokeWidth", rig["outlineWidth"])
        attrs = (
            f'fill="{part.get("fill", "none")}" stroke="{stroke}" stroke-width="{sw}" '
            f'stroke-linejoin="round" stroke-linecap="round" opacity="{part.get("opacity", 1)}" '
            f'transform="{group_transform(rig, pose, grp)}"'
        )
        if "ellipse" in part:
            cx, cy, rx, ry = part["ellipse"]
            out.append(f'<ellipse cx="{cx}" cy="{cy}" rx="{rx}" ry="{ry}" {attrs}/>')
        else:
            out.append(f'<path d="{part["path"]}" {attrs}/>')
    if flip:
        out.append("</g>")
    out.append("</svg>")
    return "\n".join(out)


def main():
    args = sys.argv[1:]
    hidden = set()
    flip = False
    if "--hidden" in args:
        i = args.index("--hidden")
        hidden = set(args[i + 1].split(","))
        del args[i:i + 2]
    if "--flip" in args:
        args.remove("--flip")
        flip = True
    out = args[0]
    pose = json.load(open(args[1])) if len(args) > 1 else {}
    rig = json.load(open(RIG))
    open(out, "w").write(render(rig, pose, hidden, flip))


if __name__ == "__main__":
    main()
