# Diagram Rendering Guide

Guide for maintaining and rendering GrainLedger architecture, package, class, and sequence diagrams.

## Path Conventions

Source files remain tracked in Git under `docs/SDD/`:
- **Architecture**: `docs/SDD/GrainLedger_Architecture.drawio` (draw.io source)
- **Package diagrams**: `docs/SDD/diagrams/*.puml` (3 PlantUML sources: `02_packages_field.puml`, `03_packages_workspace.puml`, `04_packages_core.puml`)
- **Class and sequence diagrams**: `docs/SDD/diagrams/*.mmd` (28 Mermaid sources)

Rendered outputs belong in `docs/SDD/diagrams/rendered/` and are **gitignored**:
- `docs/SDD/diagrams/rendered/*.svg` (32 vector files)
- `docs/SDD/diagrams/rendered/*.png` (32 raster files)

SDD Markdown references images via `diagrams/rendered/<name>.svg`.

---

## Prerequisites

1. **Mermaid CLI (`mmdc`)**:
   ```bash
   npm install -g @mermaid-js/mermaid-cli
   ```
2. **PlantUML and Graphviz**:
   - Java JRE/JDK (version 11+)
   - Graphviz (`dot` command)
   - PlantUML package (`plantuml` binary) or `plantuml.jar`
3. **draw.io Desktop CLI (`drawio`)**:
   - `drawio` desktop application / CLI on PATH.
4. **SDD Exporter (Optional)**:
   - Python 3 with `python-docx`, `markdown`, and `Pillow`.

---

## Render Commands

Run all commands from repo root.

### 1. Mermaid Diagrams (`.mmd`)

Config file: `docs/SDD/tools/mermaid-config.json`

#### Single Diagram
```bash
mmdc -i docs/SDD/diagrams/05_class.mmd -o docs/SDD/diagrams/rendered/05_class.svg -c docs/SDD/tools/mermaid-config.json
mmdc -i docs/SDD/diagrams/05_class.mmd -o docs/SDD/diagrams/rendered/05_class.png -c docs/SDD/tools/mermaid-config.json
```

#### Batch Render All 28 Mermaid Diagrams
```bash
mkdir -p docs/SDD/diagrams/rendered
for file in docs/SDD/diagrams/*.mmd; do
  base=$(basename "$file" .mmd)
  mmdc -i "$file" -o "docs/SDD/diagrams/rendered/${base}.svg" -c docs/SDD/tools/mermaid-config.json
  mmdc -i "$file" -o "docs/SDD/diagrams/rendered/${base}.png" -c docs/SDD/tools/mermaid-config.json
done
```

---

### 2. PlantUML Package Diagrams (`.puml`)

#### Via Helper Script
Auto-detects system PlantUML jar (e.g. `/usr/share/java/plantuml/plantuml.jar`) or accepts `--jar`:
```bash
python docs/SDD/tools/render_packages_plantuml.py
```

Explicit options if required:
```bash
python docs/SDD/tools/render_packages_plantuml.py \
  --jar /path/to/plantuml.jar \
  --java /path/to/java \
  --dot /path/to/dot \
  --out docs/SDD/diagrams/rendered
```

#### Via Direct PlantUML CLI
```bash
plantuml -tsvg -o rendered docs/SDD/diagrams/*.puml
plantuml -tpng -o rendered docs/SDD/diagrams/*.puml
```

---

### 3. draw.io System Architecture (`.drawio`)

Render `docs/SDD/GrainLedger_Architecture.drawio` to both SVG and PNG:
```bash
mkdir -p docs/SDD/diagrams/rendered
drawio -x -f svg -o docs/SDD/diagrams/rendered/01_system_architecture.svg docs/SDD/GrainLedger_Architecture.drawio
drawio -x -f png -o docs/SDD/diagrams/rendered/01_system_architecture.png docs/SDD/GrainLedger_Architecture.drawio
```

---

### 4. Rebuild SDD Artifacts (DOCX and HTML)

After updating rendered assets in `docs/SDD/diagrams/rendered/`, compile the documents:
```bash
python docs/SDD/tools/export_sdd.py
```
This updates `docs/SDD/GrainLedger_SDD.docx` and `docs/SDD/GrainLedger_SDD.html` with newly rendered figures.
