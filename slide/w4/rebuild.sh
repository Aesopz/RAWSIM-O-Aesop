set -e
rm -rf u && python -c "import sys,zipfile; zipfile.ZipFile('src.pptx').extractall('u')"
PYTHONUTF8=1 python -c "
import io
from defusedxml import minidom
p='u/ppt/slides/slide2.xml'
d=minidom.parse(p); t=d.getElementsByTagName('p:spTree')[0]
k=[sp for sp in list(t.childNodes) if sp.nodeName in ('p:sp','p:pic')
   and sp.getElementsByTagName('p:cNvPr')
   and sp.getElementsByTagName('p:cNvPr')[0].getAttribute('id').isdigit()
   and int(sp.getElementsByTagName('p:cNvPr')[0].getAttribute('id'))>600]
for x in k: t.removeChild(x)
io.open(p,'w',encoding='utf-8',newline='').write(d.toxml())
print('清掉舊 p.25 內容 %d 個形狀'%len(k))
"
PYTHONUTF8=1 PYTHONIOENCODING=utf-8 python build_m4_single.py
PYTHONUTF8=1 PYTHONIOENCODING=utf-8 python -c "import check2,sys; sys.exit(check2.main('u/ppt/slides/slide2.xml'))"
