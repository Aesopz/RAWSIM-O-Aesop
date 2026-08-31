set -e
rm -rf u && python -c "import sys,zipfile; zipfile.ZipFile('src.pptx').extractall('u')"
PYTHONUTF8=1 python "C:\Users\Aesop\.claude\plugins\cache\anthropic-agent-skills\document-skills\f379e5ad66e2\skills\pptx/scripts/add_slide.py" u/ slide2.xml --after slide2.xml >/dev/null
PYTHONUTF8=1 python -c "
import io,re
from defusedxml import minidom
p='u/ppt/slides/slide3.xml'
s=io.open(p,encoding='utf-8').read()
m=re.search(r'(sz=\"3200\"[^>]*>.*?<a:t>)(.*?)(</a:t>)',s,re.S)
s=s[:m.start(2)]+'Solving M4 &#8212; Dinkelbach Transform and Self-Calibrated Prices'+s[m.end(2):]
s=re.sub(r'<a:t>第 \d+ 頁</a:t>','<a:t>第 26 頁</a:t>',s)
io.open(p,'w',encoding='utf-8',newline='').write(s)
d=minidom.parse(p); t=d.getElementsByTagName('p:spTree')[0]
for k in [sp for sp in list(t.childNodes) if sp.nodeName in ('p:sp','p:pic')
          and sp.getElementsByTagName('p:cNvPr')
          and sp.getElementsByTagName('p:cNvPr')[0].getAttribute('id').isdigit()
          and int(sp.getElementsByTagName('p:cNvPr')[0].getAttribute('id'))>600]:
    t.removeChild(k)
io.open(p,'w',encoding='utf-8',newline='').write(d.toxml())
"
PYTHONUTF8=1 PYTHONIOENCODING=utf-8 python build_p26.py
PYTHONUTF8=1 PYTHONIOENCODING=utf-8 python -c "import check2,sys; sys.exit(check2.main('u/ppt/slides/slide3.xml'))"
