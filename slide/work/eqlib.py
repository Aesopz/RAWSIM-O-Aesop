# -*- coding: utf-8 -*-
"""LaTeX -> PNG for PowerPoint, plus the package bookkeeping an image needs.

Equations are rendered with matplotlib's mathtext at STIX (a Times-metric face,
so they sit with the deck's Times New Roman body) and placed at their NATURAL
size: rendering at `fs` points and DPI d gives an image of px/d inches whose
glyphs are fs points tall, so no scaling is ever applied and nothing is blurry.
"""
import hashlib, io, os, re

import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
from PIL import Image

plt.rcParams["mathtext.fontset"] = "stix"

E = 914400.0
DPI = 400
MEDIA = "unpacked/ppt/media"
RELS = "unpacked/ppt/slides/_rels"

_cache = {}


def emu(v):
    return int(round(v * E))


def render(latex, fs=17, color="#404040"):
    """Render one expression. Returns (media filename, width_in, height_in)."""
    key = hashlib.md5(("%s|%s|%s" % (latex, fs, color)).encode("utf-8")).hexdigest()[:16]
    name = "eq_%s.png" % key
    path = os.path.join(MEDIA, name)
    if key not in _cache:
        if not os.path.exists(path):
            fig = plt.figure(figsize=(0.01, 0.01))
            fig.text(0, 0, "$%s$" % latex, fontsize=fs, color=color)
            fig.savefig(path, dpi=DPI, transparent=True, bbox_inches="tight",
                        pad_inches=0.02)
            plt.close(fig)
        with Image.open(path) as im:
            w, h = im.size
        _cache[key] = (name, w / float(DPI), h / float(DPI))
    return _cache[key]


class SlideRels(object):
    """Appends image relationships to one slide's .rels, reusing an existing
    entry when the same media file is added twice."""

    def __init__(self, slide_file):
        self.path = os.path.join(RELS, slide_file + ".rels")
        self.xml = io.open(self.path, encoding="utf-8").read()
        used = [int(m) for m in re.findall(r'Id="rId(\d+)"', self.xml)]
        self.next = (max(used) + 1) if used else 1
        self.by_target = dict(re.findall(r'Id="(rId\d+)" Target="\.\./media/([^"]+)"',
                                         self.xml))
        self.by_target = {v: k for k, v in self.by_target.items()}

    def image(self, media_name):
        if media_name in self.by_target:
            return self.by_target[media_name]
        rid = "rId%d" % self.next
        self.next += 1
        self.by_target[media_name] = rid
        entry = ('<Relationship Id="%s" Target="../media/%s" '
                 'Type="http://schemas.openxmlformats.org/officeDocument/2006/'
                 'relationships/image"/>' % (rid, media_name))
        self.xml = self.xml.replace("</Relationships>", entry + "</Relationships>")
        return rid

    def save(self):
        io.open(self.path, "w", encoding="utf-8", newline="").write(self.xml)


def pic(shape_id, rid, x, y, w, h):
    """A picture shape holding a rendered equation."""
    return ('<p:pic><p:nvPicPr><p:cNvPr id="%d" name="Equation %d"/>'
            '<p:cNvPicPr><a:picLocks noChangeAspect="1"/></p:cNvPicPr><p:nvPr/>'
            '</p:nvPicPr><p:blipFill><a:blip r:embed="%s"/>'
            '<a:stretch><a:fillRect/></a:stretch></p:blipFill>'
            '<p:spPr><a:xfrm><a:off x="%d" y="%d"/><a:ext cx="%d" cy="%d"/></a:xfrm>'
            '<a:prstGeom prst="rect"><a:avLst/></a:prstGeom></p:spPr></p:pic>'
            % (shape_id, shape_id, rid, emu(x), emu(y), emu(w), emu(h)))
