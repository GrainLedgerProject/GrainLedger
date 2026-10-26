"""Render only the three SDD package diagrams with PlantUML/Graphviz.
Usage: python tools/render_packages_plantuml.py [--jar /path/to/plantuml.jar] [--java /path/to/java] [--dot /path/to/dot] [--out /path/to/output_dir]
Writes SVG and PNG into diagrams/rendered/. Requires Java and Graphviz.
"""
from pathlib import Path
import argparse,subprocess,os
default_jar='/usr/share/java/plantuml/plantuml.jar' if Path('/usr/share/java/plantuml/plantuml.jar').exists() else None
parser=argparse.ArgumentParser()
parser.add_argument('--jar',default=default_jar,required=default_jar is None,help='Path to plantuml.jar')
parser.add_argument('--java',default='java',help='Path to java binary')
parser.add_argument('--dot',help='Path to Graphviz dot binary')
parser.add_argument('--out',help='Output directory (defaults to docs/SDD/diagrams/rendered)')
args=parser.parse_args()
r=Path(__file__).resolve().parents[1];sources=[r/'diagrams'/(n+'.puml') for n in ['02_packages_field','03_packages_workspace','04_packages_core']]
out_dir=Path(args.out).resolve() if args.out else r/'diagrams'/'rendered'
out_dir.mkdir(parents=True,exist_ok=True)
env=os.environ.copy()
if args.dot:env['GRAPHVIZ_DOT']=args.dot
for fmt in ['svg','png']:
 subprocess.run([args.java,'-Djava.awt.headless=true','-jar',str(Path(args.jar).resolve()),'-failfast2','-t'+fmt,'-o',str(out_dir),*map(str,sources)],env=env,check=True)
print(f'PlantUML: three package diagrams exported to SVG and PNG in {out_dir}.')
