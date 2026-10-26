"""Generate native editable SVG/draw.io architecture and UML package diagrams."""
from pathlib import Path
import json,textwrap,html,xml.etree.ElementTree as E
import cairosvg
R=Path(__file__).resolve().parents[1];D=R/'diagrams'
mx=E.Element('mxfile',host='app.diagrams.net')
class Canvas:
 def __init__(self,name,w,h):
  self.name=name;self.w=w;self.h=h;self.s=[f'<svg xmlns="http://www.w3.org/2000/svg" width="{w}" height="{h}" viewBox="0 0 {w} {h}"><defs><marker id="a" markerWidth="8" markerHeight="8" refX="7" refY="4" orient="auto"><path d="M0,0 L8,4 L0,8 Z"/></marker></defs><rect width="100%" height="100%" fill="white"/>']
  page=E.SubElement(mx,'diagram',id=name,name=name);model=E.SubElement(page,'mxGraphModel',dx=str(w),dy=str(h),grid='1',gridSize='10',page='1',pageWidth=str(w),pageHeight=str(h));self.root=E.SubElement(model,'root');E.SubElement(self.root,'mxCell',id='0');E.SubElement(self.root,'mxCell',id='1',parent='0');self.id=1
 def cell(self,label,x,y,w,h,style):
  self.id+=1;c=E.SubElement(self.root,'mxCell',id=str(self.id),value=label,style=style,vertex='1',parent='1');E.SubElement(c,'mxGeometry',x=str(x),y=str(y),width=str(w),height=str(h),**{'as':'geometry'})
 def text(self,x,y,label,size=21,bold=False):
  self.s.append(f'<text x="{x}" y="{y}" text-anchor="middle" font-family="Arial,sans-serif" font-size="{size}"'+(' font-weight="bold"' if bold else '')+'>'+html.escape(label)+'</text>')
 def box(self,x,y,w,h,label,fill='white',size=20):
  self.s.append(f'<rect x="{x}" y="{y}" width="{w}" height="{h}" fill="{fill}" stroke="#333"/>')
  lines=[]
  for l in label.split('\n'):lines+=textwrap.wrap(l,max(12,int(w/(size*.52))))
  yy=y+h/2-(len(lines)-1)*(size+6)/2+size/3
  for l in lines:self.text(x+w/2,yy,l,size);yy+=size+6
  self.cell(label,x,y,w,h,f'rounded=0;whiteSpace=wrap;html=1;fillColor={fill};strokeColor=#333333;fontSize={size};fontFamily=Arial;')
 def group(self,x,y,w,h,label,color):
  self.s.append(f'<rect x="{x}" y="{y}" width="{w}" height="{h}" fill="{color}" stroke="#777"/>');self.text(x+w/2,y+32,label,23,True)
  self.cell(label,x,y,w,h,f'whiteSpace=wrap;html=1;fillColor={color};strokeColor=#777777;fontSize=23;fontStyle=1;verticalAlign=top;spacingTop=8;')
 def package(self,x,y,w,h,label,color='white',parent=False):
  label=label.replace('DependencyInjection','Dependency Injection')
  tab=min(w*.30,140);self.s.append(f'<path d="M{x},{y+32} V{y} H{x+tab} V{y+32} H{x+w} V{y+h} H{x} Z" fill="{color}" stroke="#444"/>')
  if parent:self.text(x+w/2,y+61,label,23,True)
  else:
   lines=textwrap.wrap(label,int(w/11)) or [label];yy=y+32+(h-32)/2-(len(lines)-1)*12+7
   for l in lines:self.text(x+w/2,yy,l,20,True);yy+=25
  self.cell(label,x,y,w,h,f'shape=folder;tabWidth={tab};tabHeight=32;whiteSpace=wrap;html=1;fillColor={color};strokeColor=#444444;fontSize=20;fontStyle=1;verticalAlign='+('top;spacingTop=36;' if parent else 'middle;'))
 def arrow(self,pts,label='',dash=False):
  d='M'+' L'.join(f'{x},{y}' for x,y in pts)
  self.s.append(f'<path d="{d}" fill="none" stroke="#111" stroke-width="1.7" marker-end="url(#a)"'+(' stroke-dasharray="7 5"' if dash else '')+'/>')
  if label:
   start,end=max(zip(pts,pts[1:]),key=lambda pair:abs(pair[1][0]-pair[0][0])+abs(pair[1][1]-pair[0][1]))
   x,y=(start[0]+end[0])/2,(start[1]+end[1])/2
   self.text(x,y-9,label,17)
  self.id+=1;c=E.SubElement(self.root,'mxCell',id=str(self.id),value=label,style='edgeStyle=orthogonalEdgeStyle;rounded=0;endArrow=block;endFill=1;html=1;'+('dashed=1;' if dash else '')+'fontSize=17;',edge='1',parent='1');g=E.SubElement(c,'mxGeometry',relative='1',**{'as':'geometry'});E.SubElement(g,'mxPoint',x=str(pts[0][0]),y=str(pts[0][1]),**{'as':'sourcePoint'});E.SubElement(g,'mxPoint',x=str(pts[-1][0]),y=str(pts[-1][1]),**{'as':'targetPoint'})
  if len(pts)>2:
   ar=E.SubElement(g,'Array',**{'as':'points'})
   for x,y in pts[1:-1]:E.SubElement(ar,'mxPoint',x=str(x),y=str(y))
 def save(self):
  self.s.append('</svg>');p=D/(self.name+'.svg');p.write_text(''.join(self.s));cairosvg.svg2png(url=str(p),write_to=str(p.with_suffix('.png')),scale=2)
# Overall architecture uses the example's layered composition without adopting its technology vendors.
c=Canvas('01_system_architecture',1440,1090);c.text(720,38,'GrainLedger System Architecture',29,True)
c.group(30,150,290,370,'Frontend','#dbe8fb');c.box(60,215,230,115,'Operations and Compliance Website');c.box(60,365,230,115,'Offline-capable Field Web Client')
c.group(470,115,500,700,'Backend — Modular Application','#e6dcf0')
for y,label in [(195,'GrainLedger.API'),(355,'GrainLedger.Application'),(515,'GrainLedger.Domain'),(675,'GrainLedger.Infrastructure')]:c.box(530,y,380,90,label)
c.arrow([(720,285),(720,355)]);c.arrow([(720,445),(720,515)]);c.arrow([(720,675),(720,605)])
c.arrow([(320,260),(530,260)],'HTTPS requests')
c.arrow([(530,300),(320,300)],'Results / findings',True)
c.arrow([(320,420),(410,420),(410,335),(530,335)],'Sync / queries')
c.group(1070,150,340,370,'External Information','#f8d8d3');c.box(1100,215,280,110,'Laboratory Reports');c.box(1100,365,280,110,'MRL Publications')
c.arrow([(1100,215),(1040,215),(1040,80),(175,80),(175,215)],'',True)
c.arrow([(1100,420),(1020,420),(1020,100),(305,100),(305,215)],'',True)
c.text(690,75,'Human-mediated entry: Customer Manager / Mill Administrator',18)
c.text(1240,500,'No direct provider API assumed',17)
c.group(30,625,290,300,'Build and Deployment','#efefef');c.box(60,685,230,80,'CI/CD Build and Test');c.box(60,805,230,80,'Containerized Delivery');c.arrow([(175,765),(175,805)])
c.arrow([(290,845),(395,845),(395,740),(470,740)],'Deploy',True)
c.group(470,900,500,150,'Transactional Persistence','#d6edf8');c.text(720,980,'Stock, reservations, evidence and events',21);c.text(720,1017,'Immutable snapshots, dossiers and notices',20)
c.arrow([(650,765),(650,900)],'Protected writes');c.arrow([(805,900),(805,765)],'Scoped reads',True)
c.group(1070,625,340,300,'Immutable Content','#dcebd4');c.box(1100,690,280,135,'Retained payloads, reports, reconciliation and decision bases');c.arrow([(970,735),(1100,735)],'Persist / retrieve')
c.text(720,1080,'Immutable content may share transactional storage. Runtime technologies remain implementation choices.',17);c.save()
# Backend UML packages correspond to the actual package descriptions.
c=Canvas('04_packages_core',1600,960);c.text(800,40,'GrainLedger Backend Package Diagram',29,True)
blocks=[(50,100,710,240,'GrainLedger.API','#fff2cc',['Controllers','Middlewares']),(840,100,710,300,'GrainLedger.Domain','#dbe8fb',['Entities','Enums','Policies']),(50,430,710,465,'GrainLedger.Application','#f8cecc',['Commons','DTOs','Interfaces','Services','Mapping','Validations']),(840,470,710,425,'GrainLedger.Infrastructure','#d5e8d4',['Repositories','Persistence','Content','DependencyInjection'])]
for x,y,w,h,title,color,children in blocks:
 c.package(x,y,w,h,title,color,True)
 for j,label in enumerate(children):c.package(x+35+(j%3)*225,y+105+(j//3)*150,190,105,label)
c.arrow([(405,340),(405,430)],dash=True);c.arrow([(760,600),(800,600),(800,260),(840,260)],dash=True);c.arrow([(1190,470),(1190,400)],dash=True);c.arrow([(840,780),(760,780)],dash=True)
c.text(800,943,'Dependency direction: API → Application → Domain; Infrastructure → Application interfaces and Domain.',19);c.save()
# Both frontend subsystems use true UML package tabs.
for name,title,rows in json.loads((R/'tools/package_descriptions.json').read_text())[1:]:
 cols=4;count=len(rows);height=180+((count+cols-1)//cols)*180
 c=Canvas(name,1500,height);c.text(750,42,'GrainLedger '+title,28,True);c.package(35,85,1430,height-120,'Website' if 'workspace' in name else 'Field Web Client','#fff2cc',True)
 for j,(label,desc) in enumerate(rows):c.package(80+(j%cols)*350,195+(j//cols)*165,290,105,label)
 # Selected source-code dependencies, routed through inter-row gutters.
 if 'workspace' in name:
  c.arrow([(430,248),(370,248)],dash=True)
  c.arrow([(575,300),(575,330),(225,330),(225,360)],dash=True)
  c.arrow([(1275,195),(1275,178),(575,178),(575,195)],dash=True)
  c.arrow([(925,300),(925,345),(575,345),(575,360)],dash=True)
 else:
  c.arrow([(370,248),(430,248)],dash=True)
  c.arrow([(1130,248),(1070,248)],dash=True)
  c.arrow([(1275,300),(1275,330),(575,330),(575,360)],dash=True)
  c.arrow([(225,300),(225,345),(925,345),(925,300)],dash=True)
 c.save()
E.ElementTree(mx).write(R/'GrainLedger_Architecture_Packages.drawio',encoding='utf-8',xml_declaration=True)
print('Created layered architecture, three UML package diagrams and four editable draw.io pages.')
